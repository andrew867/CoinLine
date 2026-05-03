namespace HostPlatform.Protocols.Ncc;

/// <summary>Control byte masks from <c>MTR212/NCC.H</c> (receive validation in <c>NCCASM.ASM</c>).</summary>
public static class NccControl
{
    public const byte PacketMask = 0x03;
    public const byte ReTrans = 0x04;
    public const byte Ack = 0x08;
    public const byte Nack = 0x10;
    public const byte Clr = 0x20;
    public const byte ConMask = 0x38;
    public const byte AckNackMask = 0x18;
    public const byte AckNackSpareMask = 0xD8;
    public const byte SpareMask = 0xC0;

    public static byte PacketId(byte control) => (byte)(control & PacketMask);

    /// <summary>Non-zero <see cref="ConMask"/> bits ⇒ control packet path (no termid/msg on wire).</summary>
    public static bool IsControlPacket(byte control) => (control & ConMask) != 0;

    public static bool HasBothAckAndNack(byte control) => (control & AckNackMask) == AckNackMask;
}
