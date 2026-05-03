namespace HostPlatform.Protocols.Ncc;

public static class NccConstants
{
    public const byte FrameStart = 0x02; // STX / NCC_FRAME_START
    public const byte FrameEnd = 0x03;   // ETX / NCC_FRAME_END
    public const int TerminalIdSize = 5; // NCC_TERM_SIZE
    public const int MaxDataSize = 245;  // NCC_MAX_MSG_SIZE

    /// <summary><c>NCC_CONPKT_LEN</c> — control packet <c>count</c> must equal this.</summary>
    public const byte ControlPacketCount = 5;

    /// <summary><c>NCC_MIN_MSGPKT_LEN</c> — minimum <c>count</c> for a message packet.</summary>
    public const byte MinMessagePacketCount = 11;

    /// <summary><c>NCC_CONPKT_LEN</c> — overhead bytes in <c>count</c> (ctl+cnt semantics + term slot accounting).</summary>
    public const int ControlPayloadOverhead = 5;

    /// <summary>Shortest on-air frame (control packet: STX, ctl, cnt, CRC×2, ETX).</summary>
    public const int MinWireLength = 6;

    /// <summary><c>NCC_MAX_PKT_SIZE</c> — upper bound for a single frame on the wire.</summary>
    public const int MaxWireLength = 256;
}
