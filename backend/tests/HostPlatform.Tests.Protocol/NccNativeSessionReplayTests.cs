using System.Security.Cryptography;
using System.Text;
using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Tests.Protocol;

internal sealed class NativeNccCaptureFactAttribute : FactAttribute
{
    public NativeNccCaptureFactAttribute()
    {
        if (!File.Exists(NccNativeSessionReplayTests.CapturePath) && !File.Exists(NccNativeSessionReplayTests.HashPath))
            Skip = "Pending a complete reviewed native captured NCC session shared with the emulator.";
    }
}

public sealed class NccNativeSessionReplayTests
{
    internal static string CapturePath => Path.Combine(AppContext.BaseDirectory, "fixtures", "ncc", "streams", "native-session.tsv");
    internal static string HashPath => CapturePath + ".sha256";
    private sealed record Frame(char Direction, byte[] Raw, NccWirePacket Packet);

    [NativeNccCaptureFact]
    public void Shared_native_capture_replays_exact_frames_and_both_directions()
    {
        Assert.True(File.Exists(CapturePath) && File.Exists(HashPath), "The capture and its SHA-256 sidecar must both be present.");
        Verify(File.ReadAllBytes(CapturePath), File.ReadAllBytes(HashPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generated_gate_control_accepts_a_complete_reviewable_dialogue(bool clock)
    {
        var (tsv, hash) = Serialize(Generated(clock));
        Verify(tsv, hash);
    }

    [Fact]
    public void Gate_rejects_wrong_hash_malformed_wire_incomplete_and_unreviewed_dialogues()
    {
        var lines = Generated(false);
        var (valid, hash) = Serialize(lines);
        Assert.Throws<InvalidDataException>(() => Verify(valid, []));
        var changed = valid.ToArray(); changed[^2] = (byte)'7';
        Assert.Throws<InvalidDataException>(() => Verify(changed, hash));
        var crlf = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(valid).Replace("\n", "\r\n"));
        Assert.Throws<InvalidDataException>(() => Verify(crlf, Digest(crlf)));
        Reject(lines[..^1]);
        Reject(lines.Select((frame, index) => index == 1 ? ('T', frame.Raw) : frame));
        var corrupt = Generated(false); corrupt[0].Raw[^2] ^= 1; Reject(corrupt);
        var excluded = Generated(false); excluded[0] = ('T', Encode(0, [6, 0, 0, 0, 0, 0])); Reject(excluded);

        static void Reject(IEnumerable<(char Direction, byte[] Raw)> frames)
        {
            var (tsv, digest) = Serialize(frames);
            Assert.Throws<InvalidDataException>(() => Verify(tsv, digest));
        }
    }

    private static void Verify(byte[] tsv, byte[] hash)
    {
        Require(tsv.Length is > 14 and <= 65536 && tsv.All(value => value is 9 or 10 or >= 32 and <= 126), "The capture is not canonical ASCII TSV.");
        Require(hash.AsSpan().SequenceEqual(Digest(tsv)), "The capture SHA-256 does not match its canonical sidecar.");
        var rows = Encoding.ASCII.GetString(tsv).Split('\n');
        Require(rows[0] == "direction\thex" && rows[^1].Length == 0, "Invalid capture header or missing final LF.");
        var frames = new List<Frame>();
        foreach (var row in rows[1..^1])
        {
            Require(row.Length >= 14 && row[0] is 'T' or 'H' && row[1] == '\t', "Invalid direction or frame row.");
            var hex = row[2..];
            Require(hex.Length % 2 == 0 && hex.All(value => value is >= '0' and <= '9' or >= 'A' and <= 'F'), "Frames must use uppercase paired hexadecimal.");
            var wire = Convert.FromHexString(hex);
            var decoded = NccFrameCodec.TryDecode(wire, NccParseMode.Strict);
            Require(decoded.Success && decoded.Packet != null, "A recorded frame failed strict NCC parsing.");
            var packet = decoded.Packet!;
            Require(wire.AsSpan().SequenceEqual(NccFrameCodec.Encode(packet)), "A recorded frame did not re-encode byte for byte.");
            frames.Add(new(row[0], wire, packet));
        }
        ValidateDialogue(frames);

        foreach (var direction in new[] { 'T', 'H' })
        {
            var expected = frames.Where(frame => frame.Direction == direction).ToArray();
            var stream = expected.SelectMany(frame => frame.Raw).ToArray();
            VerifyFrames(expected, NccStreamReader.ReadOrdered(stream, NccParseMode.Strict));
            foreach (var chunkSizes in new[] { new[] { 1 }, new[] { 2, 7, 1, 13, 3, 32, 5 } })
            {
                var decoder = new NccIncrementalStreamDecoder(NccParseMode.Strict);
                var result = new List<NccStreamOrderedItem>();
                for (int offset = 0, index = 0; offset < stream.Length; index++)
                {
                    var size = Math.Min(chunkSizes[index % chunkSizes.Length], stream.Length - offset);
                    result.AddRange(decoder.Feed(stream.AsSpan(offset, size))); offset += size;
                }
                Require(decoder.PendingByteCount == 0 && decoder.Flush().Count == 0, "The recorded stream left an incomplete frame.");
                VerifyFrames(expected, result);
            }
        }
    }

    private static void ValidateDialogue(IReadOnlyList<Frame> frames)
    {
        var firstMessage = frames.FirstOrDefault(frame => frame.Packet.Data.Length > 0);
        Require(firstMessage != null, "No terminal dialogue was recorded.");
        var terminal = firstMessage!.Packet.TerminalId;
        Require(terminal.AsSpan().SequenceEqual(Convert.FromHexString("2025550197")) ||
                terminal.AsSpan().SequenceEqual(Convert.FromHexString("2025550199")), "The capture does not use a reviewed synthetic identity.");
        foreach (var frame in frames.Where(frame => frame.Packet.Data.Length > 0))
        {
            Require(frame.Packet.TerminalId.AsSpan().SequenceEqual(terminal), "A dialogue frame has a different identity.");
            var data = frame.Packet.Data;
            var allowed = frame.Direction == 'T' && (data.AsSpan().SequenceEqual(new byte[] { 8 }) || data.AsSpan().SequenceEqual(new byte[] { 36 })) ||
                frame.Direction == 'H' && (data.AsSpan().SequenceEqual(new byte[] { 13 }) || data.Length == 8 && data[0] == 20);
            // Reviewed alarm-only shape: time, alarm, status, version, EOD. No maintenance identifiers.
            allowed |= frame.Direction == 'T' && data.Length == 48 && data[0] == 7 && data[7] == 24 &&
                terminal.AsSpan().SequenceEqual(Convert.FromHexString("2025550199")) &&
                data[1..7].All(value => (value >> 4) <= 9 && (value & 15) <= 9) && data[8] == 10 &&
                data.AsSpan(9, 10).SequenceEqual(Convert.FromHexString("01020304080001000100")) && data[19] == 60 && data[47] == 13 &&
                Convert.ToHexString(SHA256.HashData(data.AsSpan(19, 28))) == "7151B142A863EEFED2B1288C400973F6B286C0D17D0D040B0D32D4F0738BFAB5";
            allowed |= frame.Direction == 'H' && data.AsSpan().SequenceEqual(new byte[] { 16, 24, 13 });
            Require(allowed, "The capture contains an unsupported or excluded application record.");
        }

        (char, byte, int)[] routine = [('T', 0, 8), ('H', 8, -1), ('H', 0, 13)];
        (char, byte, int)[] clock = [('T', 0, 36), ('H', 8, -1), ('H', 0, 20), ('T', 8, -1), ('H', 1, 13)];
        (char, byte, int)[] alarm = [('T', 0, 7), ('H', 8, -1), ('H', 0, 16)];
        var observed = frames.Select(frame => (frame.Direction, frame.Packet.Control, frame.Packet.Data.Length == 0 ? -1 : frame.Packet.Data[0])).ToArray();
        Require(Complete(routine, 8) || Complete(clock, 9) || Complete(alarm, 8),
            "The capture is not a complete reviewed dialogue with final clear.");

        bool Complete((char, byte, int)[] prefix, byte ack) =>
            observed.SequenceEqual(prefix.Concat(new (char, byte, int)[] { ('T', ack, -1), ('T', 32, -1) }));
    }

    private static void VerifyFrames(Frame[] expected, IReadOnlyList<NccStreamOrderedItem> actual)
    {
        Require(expected.Length == actual.Count, "Stream replay returned a different number of items.");
        for (var i = 0; i < expected.Length; i++)
            Require(actual[i] is NccStreamParsedFrame parsed && parsed.Frame.Parse.Success && !parsed.Frame.IsTruncated &&
                parsed.Frame.RawBytes.AsSpan().SequenceEqual(expected[i].Raw), "Stream replay changed a frame or emitted a gap.");
    }

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidDataException(error);
    }

    // Generated controls test the validator, not the native capture.
    private static (char Direction, byte[] Raw)[] Generated(bool clock) => clock
        ? [('T', Encode(0, [36])), ('H', Encode(8, [])), ('H', Encode(0, [20, 0, 0, 0, 0, 0, 0, 0])),
           ('T', Encode(8, [])), ('H', Encode(1, [13])), ('T', Encode(9, [])), ('T', Encode(32, []))]
        : [('T', Encode(0, [8])), ('H', Encode(8, [])), ('H', Encode(0, [13])), ('T', Encode(8, [])), ('T', Encode(32, []))];

    private static byte[] Encode(byte control, byte[] data) => NccFrameCodec.Encode(new NccWirePacket
    {
        FrameStart = NccConstants.FrameStart, Control = control, Count = 0, TerminalId = Convert.FromHexString("2025550197"),
        Data = data, Crc = 0, FrameEnd = NccConstants.FrameEnd
    });

    private static (byte[] Tsv, byte[] Hash) Serialize(IEnumerable<(char Direction, byte[] Raw)> frames)
    {
        var tsv = Encoding.ASCII.GetBytes("direction\thex\n" + string.Concat(frames.Select(frame => $"{frame.Direction}\t{Convert.ToHexString(frame.Raw)}\n")));
        return (tsv, Digest(tsv));
    }

    private static byte[] Digest(byte[] tsv) => Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(tsv)).ToLowerInvariant() + "  native-session.tsv\n");
}
