namespace HostPlatform.Protocols.Ncc;

/// <summary>Ordered stream reconstruction: every input byte appears in exactly one item (gap or frame).</summary>
public abstract class NccStreamOrderedItem
{
    public required int StartOffset { get; init; }
}

/// <summary>
/// Bytes that occur before the next STX (line noise, non-NCC data, or sync loss).
/// Never discarded — retained for inspection. Classification is <c>HARDWARE_VALIDATION_REQUIRED</c>.
/// </summary>
public sealed class NccStreamInterFrameGap : NccStreamOrderedItem
{
    public required byte[] RawBytes { get; init; }
}

/// <summary>One NCC frame attempt at <see cref="StartOffset"/> (may be truncated or length-invalid).</summary>
public sealed class NccStreamParsedFrame : NccStreamOrderedItem
{
    public required NccStreamFrame Frame { get; init; }
}
