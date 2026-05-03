using HostPlatform.Protocols.Ncc;

/// <summary>
/// Regenerates canonical NCC wire binaries under host-platform/fixtures/ncc/.
/// Run from repo root: dotnet run --project host-platform/backend/tools/FixtureGen -- ../../../fixtures
/// </summary>
internal static class Program
{
    internal static int Main(string[] args)
    {
        // Default: host-platform/fixtures when run from tools/FixtureGen/bin/Debug/net*/ (6 levels up to host-platform).
        var fixturesRoot = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "fixtures"));
        var nccDir = Path.Combine(fixturesRoot, "ncc");
        Directory.CreateDirectory(nccDir);

        var clr = new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = NccControl.Clr,
            Count = NccConstants.ControlPacketCount,
            TerminalId = [0, 0, 0, 0, 0],
            Data = [],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        };
        var clrWire = NccFrameCodec.Encode(clr);
        WriteBinHex(nccDir, "control_clr", clrWire);

        var msgMin = new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = 0,
            Count = 0,
            TerminalId = [0x01, 0x02, 0x03, 0x04, 0x05],
            Data = [0xAA],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        };
        var msgWire = NccFrameCodec.Encode(msgMin);
        WriteBinHex(nccDir, "message_min", msgWire);

        Console.WriteLine($"Wrote NCC fixtures under {nccDir}");
        return 0;
    }

    private static void WriteBinHex(string dir, string baseName, byte[] wire)
    {
        File.WriteAllBytes(Path.Combine(dir, baseName + ".bin"), wire);
        File.WriteAllText(Path.Combine(dir, baseName + ".hex"),
            Convert.ToHexString(wire).ToLowerInvariant());
    }
}
