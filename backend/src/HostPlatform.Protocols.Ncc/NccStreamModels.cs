namespace HostPlatform.Protocols.Ncc;

/// <summary>One logical frame extracted from a byte stream (always preserves <see cref="RawBytes"/>).</summary>
public sealed class NccStreamFrame
{
    /// <summary>Offset of <see cref="RawBytes"/>[0] (STX) in the original stream chunk.</summary>
    public required int StartOffset { get; init; }

    /// <summary>Exact on-wire octets for this frame (or partial tail if <see cref="IsTruncated"/>).</summary>
    public required byte[] RawBytes { get; init; }

    public required NccParseResult Parse { get; init; }

    /// <summary>True if fewer bytes were available than <c>count+1</c> implied.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>Count field implied invalid wire length (resync may skip STX).</summary>
    public bool IsLengthFieldInvalid { get; init; }
}
