namespace HostPlatform.Protocols.Dlog;

public static class DlogMaintenanceWire
{
    private const byte RequestMessageType = 6;
    private const byte AckMessageType = 15;
    private const byte EndDataMessageType = 13;
    private const int RequestLength = 6;

    public static bool HasRequestRecord(ReadOnlySpan<byte> data) =>
        data.Length >= RequestLength && data[0] == RequestMessageType;

    /// <summary>Acknowledges each maintenance opcode in a complete firmware upload.</summary>
    public static bool TryBuildAcknowledgement(ReadOnlySpan<byte> data, out byte[] response)
    {
        response = [];
        var acknowledgements = new List<byte>();
        while (HasRequestRecord(data))
        {
            acknowledgements.Add(AckMessageType);
            acknowledgements.Add(data[1]);
            acknowledgements.Add(data[2]);
            data = data[RequestLength..];
        }

        if (acknowledgements.Count == 0 || !DlogFieldLayouts.HasUploadTrailer(data))
            return false;

        acknowledgements.Add(EndDataMessageType);
        response = acknowledgements.ToArray();
        return true;
    }
}
