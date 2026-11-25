namespace HostPlatform.Protocols.Dlog;

/// <summary>Reason flags on a terminal MT44 table request.</summary>
public static class DlogTableRequestWire
{
    public const byte MessageType = 44;
    public const byte CraftUpdate = 0x01;
    public const byte Install = 0x02;
    public const byte MemoryLost = 0x04;

    // An INSTALL or lost-memory request cannot rely on any previously
    // installed table. Cashbox restore is an independent request flag.
    public static bool RequiresFullAssignment(byte reason) => (reason & (Install | MemoryLost)) != 0;
}
