namespace HostPlatform.Protocols.Dlog;

public static class DlogAlarmWire
{
    public const byte AlarmMessageType = 7;
    private const byte EndDataMessageType = 13;
    private const byte AlarmAckMessageType = 16;

    /// <summary>Builds acknowledgements and end-of-data for a complete alarm upload from the supported firmware layout.</summary>
    public static bool TryBuildAcknowledgement(ReadOnlySpan<byte> data, out byte[] response)
    {
        response = [];
        var acknowledgements = new List<byte>();
        while (data is [AlarmMessageType, ..] && data.Length >= DlogFieldLayouts.AlarmMinBytes)
        {
            acknowledgements.Add(AlarmAckMessageType);
            acknowledgements.Add(data[DlogFieldLayouts.AlarmMinBytes - 1]);
            data = data[DlogFieldLayouts.AlarmMinBytes..];
        }

        if (acknowledgements.Count == 0 || !DlogFieldLayouts.HasUploadTrailer(data))
            return false;

        acknowledgements.Add(EndDataMessageType);
        response = acknowledgements.ToArray();
        return true;
    }
}
