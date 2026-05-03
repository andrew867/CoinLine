namespace HostPlatform.Protocols.Ncc;

/// <summary>
/// Length rules from <c>MTR212/NCCASM.ASM</c> receive path and <c>ncco_snd_pkt</c>:
/// on-wire length is <c>count + 1</c> (STX through ETX inclusive); CRC covers all but the last 3 octets.
/// </summary>
public static class NccFrameLayout
{
    /// <summary>Total bytes from STX through ETX inclusive for a valid <paramref name="count"/> field.</summary>
    public static int GetExpectedWireLength(byte count) => count + 1;

    /// <summary>Bytes after count through last data byte (termid prefix + message) = <c>count - NCC_CONPKT_LEN</c>.</summary>
    public static int GetTermAndDataLength(byte count) => count - NccConstants.ControlPayloadOverhead;

    public static bool IsPlausibleWireLength(int length)
    {
        return length >= NccConstants.MinWireLength && length <= NccConstants.MaxWireLength;
    }
}
