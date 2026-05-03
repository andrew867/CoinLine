namespace HostPlatform.Protocols.Ncc;

public sealed class NccParseResult
{
    public bool Success { get; init; }
    public NccWirePacket? Packet { get; init; }
    public List<string> Diagnostics { get; init; } = new();

    public static NccParseResult Ok(NccWirePacket p) => new() { Success = true, Packet = p };

    public static NccParseResult Fail(string message)
    {
        var r = new NccParseResult { Success = false };
        r.Diagnostics.Add(message);
        return r;
    }
}
