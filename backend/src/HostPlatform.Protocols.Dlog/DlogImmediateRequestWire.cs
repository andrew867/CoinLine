namespace HostPlatform.Protocols.Dlog;

/// <summary>Immediate-clear dialogues may append at most two complete call-detail records after the fixed request.</summary>
public static class DlogImmediateRequestWire
{
    public static bool TrySplit(ReadOnlySpan<byte> data, out int requestLength, out IReadOnlyList<Range> callDetails)
    {
        requestLength = data.IsEmpty ? 0 : data[0] == DlogRateWire.MessageTypeRequest
            ? DlogRateWire.RequestLength : DlogCardAuthorizationWire.RequestLength(data[0]);
        callDetails = [];
        if (requestLength == 0 || data.Length < requestLength) return false;

        var records = new List<Range>(2);
        for (var offset = requestLength; offset < data.Length;)
        {
            var length = DlogCallDetailWire.RecordLength(data[offset]);
            if (records.Count == 2 || length == 0 || length > data.Length - offset) return false;
            records.Add(offset..(offset + length));
            offset += length;
        }
        callDetails = records;
        return true;
    }
}
