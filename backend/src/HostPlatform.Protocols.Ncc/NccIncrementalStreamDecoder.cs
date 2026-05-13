using System.Runtime.InteropServices;

namespace HostPlatform.Protocols.Ncc;

/// <summary>
/// Feeds modem/UART chunks and emits fully parsed ordered stream segments (gaps + frames).
/// Retains an incomplete trailing frame until more bytes arrive — no octets dropped.
/// </summary>
public sealed class NccIncrementalStreamDecoder
{
    private readonly List<byte> _buffer = new(256);
    private readonly NccParseMode _mode;

    public NccIncrementalStreamDecoder(NccParseMode mode) => _mode = mode;

    public NccParseMode Mode => _mode;

    /// <summary>Bytes not yet consumed (includes any in-progress trailing frame).</summary>
    public int PendingByteCount => _buffer.Count;

    /// <summary>Append chunk and return newly completed ordered items (relative offsets restart at zero each call).</summary>
    public IReadOnlyList<NccStreamOrderedItem> Feed(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length > 0)
        {
            foreach (var b in chunk)
                _buffer.Add(b);
        }

        if (_buffer.Count == 0)
            return Array.Empty<NccStreamOrderedItem>();

        var span = CollectionsMarshal.AsSpan(_buffer);
        var ordered = NccStreamReader.ReadOrdered(span, _mode);
        if (ordered.Count == 0)
            return Array.Empty<NccStreamOrderedItem>();

        if (ordered[^1] is NccStreamParsedFrame { Frame.IsTruncated: true } tailTrunc)
        {
            var emitCount = ordered.Count - 1;
            var emit = new List<NccStreamOrderedItem>(emitCount);
            for (var i = 0; i < emitCount; i++)
                emit.Add(ordered[i]);

            _buffer.RemoveRange(0, tailTrunc.StartOffset);
            return RenumberOffsets(emit);
        }

        var all = ordered.ToList();
        _buffer.Clear();
        return RenumberOffsets(all);
    }

    /// <summary>Flush pending buffer through <see cref="NccStreamReader"/> as final (may emit truncated tail).</summary>
    public IReadOnlyList<NccStreamOrderedItem> Flush()
    {
        if (_buffer.Count == 0)
            return Array.Empty<NccStreamOrderedItem>();

        var span = CollectionsMarshal.AsSpan(_buffer);
        var ordered = NccStreamReader.ReadOrdered(span, _mode).ToList();
        _buffer.Clear();
        return RenumberOffsets(ordered);
    }

    private static IReadOnlyList<NccStreamOrderedItem> RenumberOffsets(IReadOnlyList<NccStreamOrderedItem> items)
    {
        // Offsets are buffer-relative; after incremental compaction each emission starts at 0 for caller simplicity.
        var list = new List<NccStreamOrderedItem>(items.Count);
        var o = 0;
        foreach (var item in items)
        {
            switch (item)
            {
                case NccStreamInterFrameGap g:
                    list.Add(new NccStreamInterFrameGap { StartOffset = o, RawBytes = g.RawBytes });
                    o += g.RawBytes.Length;
                    break;
                case NccStreamParsedFrame pf:
                    list.Add(new NccStreamParsedFrame
                    {
                        StartOffset = o,
                        Frame = new NccStreamFrame
                        {
                            StartOffset = o,
                            RawBytes = pf.Frame.RawBytes,
                            Parse = pf.Frame.Parse,
                            IsTruncated = pf.Frame.IsTruncated,
                            IsLengthFieldInvalid = pf.Frame.IsLengthFieldInvalid
                        }
                    });
                    o += pf.Frame.RawBytes.Length;
                    break;
            }
        }

        return list;
    }
}
