namespace HostPlatform.Protocols.Ncc;

/// <summary>
/// NCC wire-frame length convention:
/// on-wire length is <c>count + 1</c> (STX through ETX inclusive); CRC covers all but the last 3 octets.
/// </summary>
public static class NccFrameLayout
{
    /// <summary>Total bytes from STX through ETX inclusive for a valid <paramref name="count"/> field.</summary>
    public static int GetExpectedWireLength(byte count) => count + 1;

    /// <summary>Bytes after count through the last data byte (terminal ID prefix + message) = count minus control-packet overhead.</summary>
    public static int GetTermAndDataLength(byte count) => count - NccConstants.ControlPayloadOverhead;

    public static bool IsPlausibleWireLength(int length)
    {
        return length >= NccConstants.MinWireLength && length <= NccConstants.MaxWireLength;
    }
}
