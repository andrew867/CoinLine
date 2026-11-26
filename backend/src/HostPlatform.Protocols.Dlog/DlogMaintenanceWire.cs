namespace HostPlatform.Protocols.Dlog;

public static class DlogMaintenanceWire
{
    public const byte RequestMessageType = 6;
    internal const byte AckMessageType = 15;
    private const byte EndDataMessageType = 13;
    public const int RequestLength = 6;
    internal const int OpcodeRecordLength = 3;

    public static bool HasRequestRecord(ReadOnlySpan<byte> data) =>
        data.Length >= RequestLength && data[0] == RequestMessageType;

    /// <summary>Reads the five-digit reported identifier into a temporary buffer. This does not grant terminal or host access.</summary>
    public static bool TryReadReportedIdentifier(ReadOnlySpan<byte> record, Span<char> destination)
    {
        if (!HasRequestRecord(record) || destination.Length != 5 || (record[5] & 0x0F) != 0x0E) return false;
        for (var index = 0; index < destination.Length; index++)
        {
            var value = index % 2 == 0 ? record[3 + index / 2] >> 4 : record[3 + index / 2] & 0x0F;
            if (value > 9) return false;
            destination[index] = (char)('0' + value);
        }
        return true;
    }

    /// <summary>Canonical safe representation for persistence and display, including manually imported records.</summary>
    public static bool TryRedactRecord(ReadOnlySpan<byte> raw, int? explicitMessageType, bool? firstByteIsMessageType,
        out byte[] redacted)
    {
        redacted = [];
        var inBand = firstByteIsMessageType ?? true;
        if (explicitMessageType != RequestMessageType && (!inBand || raw.IsEmpty || raw[0] != RequestMessageType)) return false;
        if (!inBand && raw.Length >= 2)
            redacted = [RequestMessageType, raw[0], raw[1]];
        else if (inBand && raw.Length >= 3 && raw[0] == RequestMessageType)
            redacted = raw[..OpcodeRecordLength].ToArray();
        else redacted = [RequestMessageType];
        return true;
    }

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
