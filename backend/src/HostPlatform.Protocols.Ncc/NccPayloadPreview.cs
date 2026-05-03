using System.Globalization;

namespace HostPlatform.Protocols.Ncc;

/// <summary>Lightweight hex preview for embedded payloads (no DLOG parsing).</summary>
public static class NccPayloadPreview
{
    public static string ToHex(byte[]? data, int maxBytes = 64)
    {
        if (data == null || data.Length == 0)
            return "";
        var n = Math.Min(maxBytes, data.Length);
        var s = Convert.ToHexString(data.AsSpan(0, n));
        return data.Length > maxBytes ? s + "…" : s;
    }

    public static IReadOnlyDictionary<string, string> MessageMetadata(NccWirePacket p) =>
        new Dictionary<string, string>
        {
            ["controlHex"] = $"0x{p.Control:X2}",
            ["packetId"] = NccControl.PacketId(p.Control).ToString(CultureInfo.InvariantCulture),
            ["isControlPacket"] = NccControl.IsControlPacket(p.Control).ToString(),
            ["count"] = p.Count.ToString(CultureInfo.InvariantCulture),
            ["terminalIdHex"] = Convert.ToHexString(p.TerminalId),
            ["dataLength"] = p.Data.Length.ToString(CultureInfo.InvariantCulture),
            ["dataHexPreview"] = ToHex(p.Data)
        };
}
