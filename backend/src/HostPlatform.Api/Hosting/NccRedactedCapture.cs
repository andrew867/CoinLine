using System.Text;
using HostPlatform.Domain;
using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Api.Hosting;

internal static class NccRedactedCapture
{
    public const string FileName = "coinline-ncc-redacted.pcapng";
    public const string ContentType = "application/x-pcapng";

    public static byte[] Write(IEnumerable<NccSessionFrame> frames)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        WriteBlock(writer, 0x0A0D0D0A, section =>
        {
            section.Write(0x1A2B3C4D);
            section.Write((ushort)1);
            section.Write((ushort)0);
            section.Write(-1L);
        });
        WriteBlock(writer, 1, network =>
        {
            network.Write((ushort)147); // LINKTYPE_USER0
            network.Write((ushort)0);
            network.Write(1u); // At most the NCC control byte.
            WriteOption(network, 2, "CoinLine NCC (redacted)"u8);
            WriteOption(network, 0, []);
        });

        foreach (var frame in frames)
            WriteBlock(writer, 6, packet => WritePacket(packet, frame));
        return output.ToArray();
    }

    private static void WritePacket(BinaryWriter packet, NccSessionFrame frame)
    {
        var control = frame.LinkControl;
        var wireLength = frame.WireLength;
        if (control == null && NccFrameCodec.TryDecode(frame.RawBytes, NccParseMode.Strict) is { Success: true, Packet: { } legacy })
        {
            control = legacy.Control;
            wireLength = frame.RawBytes.Length;
        }

        // The interface uses the default microsecond time resolution.
        var timestamp = (ulong)((frame.CreatedAtUtc.Ticks - DateTime.UnixEpoch.Ticks) / 10);
        packet.Write(0u);
        packet.Write((uint)(timestamp >> 32));
        packet.Write((uint)timestamp);
        packet.Write(control.HasValue ? 1u : 0u);
        packet.Write((uint)(wireLength ?? 0));
        if (control is { } linkControl)
        {
            packet.Write(linkControl);
            packet.Write(new byte[3]);
        }

        // Enhanced Packet Block flags use one for inbound and two for outbound.
        WriteOption(packet, 2, frame.SentByHost ? [2, 0, 0, 0] : [1, 0, 0, 0]);
        var direction = frame.SentByHost ? "host to terminal" : "terminal to host";
        var header = control.HasValue ? $", control 0x{control:X2}, {wireLength} wire bytes" : ", header unavailable";
        WriteOption(packet, 1, Encoding.UTF8.GetBytes($"NCC frame {frame.Sequence}, {direction}{header}. Payload omitted."));
        WriteOption(packet, 0, []);
    }

    private static void WriteOption(BinaryWriter output, ushort code, ReadOnlySpan<byte> value)
    {
        output.Write(code);
        output.Write((ushort)value.Length);
        output.Write(value);
        for (var pad = value.Length; pad % 4 != 0; pad++)
            output.Write((byte)0);
    }

    private static void WriteBlock(BinaryWriter output, uint type, Action<BinaryWriter> writeBody)
    {
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writeBody(writer);
        var size = checked((uint)body.Length + 12);
        output.Write(type);
        output.Write(size);
        body.WriteTo(output.BaseStream);
        output.Write(size);
    }
}
