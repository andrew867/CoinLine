namespace HostPlatform.Protocols.Dlog;

/// <summary>One logical DLOG record as bytes (exactly as captured / stored).</summary>
public sealed class DlogFrame
{
    public required byte[] RawBytes { get; init; }
}

/// <summary>DLOG payload body (may equal <see cref="DlogFrame.RawBytes"/> or a slice).</summary>
public sealed class DlogPayload
{
    public required byte[] RawBytes { get; init; }

    /// <summary>When true, <see cref="RawBytes"/>[0] is the DLOG_MT wire type byte per reference firmware list discipline.</summary>
    public bool FirstByteIsMessageType { get; init; }
}

/// <summary>Non-authoritative decoded view — never replaces raw bytes.</summary>
public sealed class DlogDecodedMetadata
{
    public int MessageType { get; init; }
    public string MessageTypeName { get; init; } = "";
    public bool IsUnknownMessageType { get; init; }
    public bool ImmediateClear { get; init; }
    public int? CorrelationKey { get; init; }
    public string Notes { get; init; } = "";
    public IReadOnlyList<DlogParseDiagnosticEntry> Diagnostics { get; init; } = Array.Empty<DlogParseDiagnosticEntry>();
}

public sealed class DlogParseDiagnosticEntry
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Detail { get; init; } = "";
}
