namespace HostPlatform.Firmware;

public static class XmodemConstants
{
    public const byte Soh = 0x01;
    public const byte Stx = 0x02;
    public const byte Eot = 0x04;
    public const byte Ack = 0x06;
    public const byte Nak = 0x15;
    public const byte Can = 0x18;
    public const byte CrcRequest = (byte)'C';
    public const int Block128 = 128;
    public const int Block1024 = 1024;
    public const byte CpmPad = 0x1A;
}
