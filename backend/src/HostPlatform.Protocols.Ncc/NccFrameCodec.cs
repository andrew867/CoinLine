using System.Buffers.Binary;

namespace HostPlatform.Protocols.Ncc;

/// <summary>
/// Encoder/decoder for NCC on-wire framing. Length rules follow <c>NCCASM.ASM</c>
/// (control packet: <c>count==5</c>, no termid/msg bytes; message: <c>count&gt;=11</c>, termid+msg = <c>count-5</c>).
/// On-air length is <c>count+1</c> octets (STX…ETX). CRC is <see cref="NccCrc16"/> over STX through last data byte (little-endian CRC on wire).
/// </summary>
/// <remarks>
/// HARDWARE_VALIDATION_REQUIRED: field captures may still expose ambiguous <c>count</c> vs payload edges on damaged UART captures;
/// strict mode follows OEM framing specification; diagnostic retains bytes for inspection.
/// </remarks>
public static class NccFrameCodec
{
    public static NccParseResult TryDecode(ReadOnlySpan<byte> buffer, NccParseMode mode)
    {
        var diag = new List<string>();

        if (buffer.Length < 3)
        {
            diag.Add("buffer shorter than STX+control+count");
            return new NccParseResult { Success = false, Diagnostics = diag };
        }

        if (buffer[0] != NccConstants.FrameStart)
            diag.Add($"expected STX 0x{NccConstants.FrameStart:X2}, got 0x{buffer[0]:X2}");

        var control = buffer[1];
        var count = buffer[2];
        var expectedLen = NccFrameLayout.GetExpectedWireLength(count);

        if (!NccFrameLayout.IsPlausibleWireLength(expectedLen))
        {
            diag.Add($"implied wire length {expectedLen} from count=0x{count:X2} is out of range");
            return new NccParseResult { Success = false, Diagnostics = diag };
        }

        if (buffer.Length < expectedLen)
        {
            diag.Add($"truncated: expected {expectedLen} bytes for count={count}, have {buffer.Length}");
            return new NccParseResult { Success = false, Diagnostics = diag };
        }

        if (buffer.Length > expectedLen)
        {
            diag.Add($"buffer longer than frame: expected {expectedLen} bytes for count={count}, have {buffer.Length} (decode uses first frame only)");
            if (mode == NccParseMode.Strict)
                return new NccParseResult { Success = false, Diagnostics = diag };
        }

        var frame = buffer[..expectedLen];

        var end = frame[^1];
        if (end != NccConstants.FrameEnd)
            diag.Add($"expected ETX 0x{NccConstants.FrameEnd:X2}, got 0x{end:X2}");

        var crcWire = (ushort)(frame[^3] | (frame[^2] << 8));
        var computed = NccCrc16.Compute(frame[..^3]);
        if (computed != crcWire)
            diag.Add($"CRC mismatch: wire={crcWire:X4} computed={computed:X4}");

        var isCtl = NccControl.IsControlPacket(control);
        if (isCtl && count != NccConstants.ControlPacketCount)
            diag.Add($"control packet requires count={NccConstants.ControlPacketCount}, got {count}");
        if (!isCtl && count < NccConstants.MinMessagePacketCount)
            diag.Add($"message packet requires count>={NccConstants.MinMessagePacketCount}, got {count}");
        if (!isCtl && count == NccConstants.ControlPacketCount)
            diag.Add("count=5 with non-control control byte — invalid for reference firmware message path");

        var termAndLen = NccFrameLayout.GetTermAndDataLength(count);
        if (isCtl && termAndLen != 0)
            diag.Add($"control packet implies 0 term+msg bytes, count yields {termAndLen}");
        if (!isCtl && termAndLen < NccConstants.TerminalIdSize + 1)
            diag.Add($"message needs at least 6 term+msg bytes (5 termid + ≥1 data), got implied {termAndLen}");

        if (NccControl.HasBothAckAndNack(control))
            diag.Add("control byte has both ACK and NACK set (invalid per reference firmware receive checks)");
        if ((control & NccControl.SpareMask) != 0)
            diag.Add($"non-zero spare bits in control byte (0x{control:X2})");

        byte[] term;
        byte[] data;
        if (isCtl)
        {
            term = new byte[NccConstants.TerminalIdSize];
            data = Array.Empty<byte>();
        }
        else if (termAndLen < NccConstants.TerminalIdSize)
        {
            term = new byte[NccConstants.TerminalIdSize];
            data = Array.Empty<byte>();
        }
        else
        {
            var td = frame.Slice(3, termAndLen);
            term = td[..NccConstants.TerminalIdSize].ToArray();
            data = td[NccConstants.TerminalIdSize..].ToArray();
            if (data.Length > NccConstants.MaxDataSize)
                diag.Add($"data length {data.Length} exceeds NCC_MAX_MSG_SIZE");
        }

        var pkt = new NccWirePacket
        {
            FrameStart = frame[0],
            Control = control,
            Count = count,
            TerminalId = term,
            Data = data,
            Crc = crcWire,
            FrameEnd = end
        };

        var strictOk = buffer.Length == expectedLen
                       && frame[0] == NccConstants.FrameStart
                       && end == NccConstants.FrameEnd
                       && computed == crcWire
                       && !NccControl.HasBothAckAndNack(control)
                       && (control & NccControl.SpareMask) == 0
                       && (isCtl
                           ? count == NccConstants.ControlPacketCount && termAndLen == 0
                           : count >= NccConstants.MinMessagePacketCount
                             && termAndLen >= NccConstants.TerminalIdSize + 1
                             && data.Length <= NccConstants.MaxDataSize);

        if (mode == NccParseMode.Strict && !strictOk)
            return new NccParseResult { Success = false, Packet = pkt, Diagnostics = diag };

        if (diag.Count > 0 && mode == NccParseMode.DiagnosticCapture)
            return new NccParseResult { Success = true, Packet = pkt, Diagnostics = diag };

        if (diag.Count > 0)
            return new NccParseResult { Success = false, Packet = pkt, Diagnostics = diag };

        return NccParseResult.Ok(pkt);
    }

    /// <summary>Build wire frame. CRC covers buffer from STX through last data byte (per <c>ncco_snd_pkt</c> / <c>nccgen_crc</c>).</summary>
    public static byte[] Encode(NccWirePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet.TerminalId);
        if (packet.TerminalId.Length != NccConstants.TerminalIdSize)
            throw new ArgumentException("TerminalId must be 5 bytes");

        if (NccControl.IsControlPacket(packet.Control))
        {
            if (NccControl.HasBothAckAndNack(packet.Control))
                throw new ArgumentException("Control byte cannot have both ACK and NACK set");
            if ((packet.Control & NccControl.SpareMask) != 0)
                throw new ArgumentException("Control byte spare bits must be zero");
            var buf = new byte[NccFrameLayout.GetExpectedWireLength(NccConstants.ControlPacketCount)];
            buf[0] = NccConstants.FrameStart;
            buf[1] = packet.Control;
            buf[2] = NccConstants.ControlPacketCount;
            var crc = NccCrc16.Compute(buf.AsSpan(0, 3));
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(3), crc);
            buf[^1] = NccConstants.FrameEnd;
            return buf;
        }

        if ((packet.Control & NccControl.SpareMask) != 0)
            throw new ArgumentException("Control byte spare bits must be zero");
        if (packet.Data.Length < 1)
            throw new ArgumentException("Message packets require at least one data byte (NCC_MIN_MSGPKT_LEN / NCCTASK).");
        if (packet.Data.Length > NccConstants.MaxDataSize)
            throw new ArgumentException("Data too large");

        var termAndMsg = NccConstants.TerminalIdSize + packet.Data.Length;
        var count = (byte)(NccConstants.ControlPayloadOverhead + termAndMsg);
        var wireLen = NccFrameLayout.GetExpectedWireLength(count);
        var buf2 = new byte[wireLen];
        buf2[0] = NccConstants.FrameStart;
        buf2[1] = packet.Control;
        buf2[2] = count;
        packet.TerminalId.AsSpan().CopyTo(buf2.AsSpan(3));
        packet.Data.AsSpan().CopyTo(buf2.AsSpan(3 + NccConstants.TerminalIdSize));
        var crc2 = NccCrc16.Compute(buf2.AsSpan(0, wireLen - 3));
        BinaryPrimitives.WriteUInt16LittleEndian(buf2.AsSpan(wireLen - 3), crc2);
        buf2[^1] = NccConstants.FrameEnd;
        return buf2;
    }
}
