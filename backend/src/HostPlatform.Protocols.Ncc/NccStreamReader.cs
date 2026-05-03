namespace HostPlatform.Protocols.Ncc;

/// <summary>Scans arbitrary bytes for STX-aligned frames; inter-STX octets are preserved as <see cref="NccStreamInterFrameGap"/>.</summary>
public static class NccStreamReader
{
    /// <summary>
    /// Full-fidelity parse: ordered list of gaps (non-STX runs) and frame attempts. Every byte in <paramref name="stream"/> is accounted for.
    /// </summary>
    public static IReadOnlyList<NccStreamOrderedItem> ReadOrdered(ReadOnlySpan<byte> stream, NccParseMode mode)
    {
        var list = new List<NccStreamOrderedItem>();
        var i = 0;
        while (i < stream.Length)
        {
            if (stream[i] != NccConstants.FrameStart)
            {
                var gapStart = i;
                while (i < stream.Length && stream[i] != NccConstants.FrameStart)
                    i++;
                list.Add(new NccStreamInterFrameGap
                {
                    StartOffset = gapStart,
                    RawBytes = stream.Slice(gapStart, i - gapStart).ToArray()
                });
                continue;
            }

            if (i + 3 > stream.Length)
            {
                var raw = stream[i..].ToArray();
                list.Add(Wrap(TruncatedFrame(i, raw, mode)));
                break;
            }

            var count = stream[i + 2];
            var expectedLen = NccFrameLayout.GetExpectedWireLength(count);
            if (!NccFrameLayout.IsPlausibleWireLength(expectedLen))
            {
                list.Add(Wrap(InvalidLengthField(i, stream)));
                i++;
                continue;
            }

            if (i + expectedLen > stream.Length)
            {
                list.Add(Wrap(TruncatedFrame(i, stream[i..].ToArray(), mode)));
                break;
            }

            var rawFrame = stream.Slice(i, expectedLen).ToArray();
            var pr = NccFrameCodec.TryDecode(rawFrame, mode);
            list.Add(Wrap(new NccStreamFrame
            {
                StartOffset = i,
                RawBytes = rawFrame,
                Parse = pr,
                IsTruncated = false,
                IsLengthFieldInvalid = false
            }));
            i += expectedLen;
        }

        return list;
    }

    private static NccStreamParsedFrame Wrap(NccStreamFrame f) => new() { StartOffset = f.StartOffset, Frame = f };

    /// <summary>Frame-only view (gaps omitted). Prefer <see cref="ReadOrdered"/> when preserving unknown bytes.</summary>
    public static IReadOnlyList<NccStreamFrame> ReadAll(ReadOnlySpan<byte> stream, NccParseMode mode)
    {
        var ordered = ReadOrdered(stream, mode);
        var frames = new List<NccStreamFrame>(ordered.Count);
        foreach (var item in ordered)
        {
            if (item is NccStreamParsedFrame pf)
                frames.Add(pf.Frame);
        }
        return frames;
    }

    private static NccStreamFrame TruncatedFrame(int start, byte[] raw, NccParseMode mode)
    {
        var pr = NccFrameCodec.TryDecode(raw, mode);
        var diag = new List<string>(pr.Diagnostics) { "truncated: incomplete frame at end of buffer" };
        return new NccStreamFrame
        {
            StartOffset = start,
            RawBytes = raw,
            Parse = new NccParseResult { Success = false, Packet = pr.Packet, Diagnostics = diag },
            IsTruncated = true,
            IsLengthFieldInvalid = false
        };
    }

    private static NccStreamFrame InvalidLengthField(int start, ReadOnlySpan<byte> stream)
    {
        var take = Math.Min(stream.Length - start, 16);
        var raw = stream.Slice(start, take).ToArray();
        var count = raw.Length >= 3 ? raw[2] : (byte)0;
        var implied = count + 1;
        var msg = $"invalid count field: implied wire length {implied} is outside [{NccConstants.MinWireLength},{NccConstants.MaxWireLength}]";
        return new NccStreamFrame
        {
            StartOffset = start,
            RawBytes = raw,
            Parse = NccParseResult.Fail(msg),
            IsTruncated = false,
            IsLengthFieldInvalid = true
        };
    }
}
