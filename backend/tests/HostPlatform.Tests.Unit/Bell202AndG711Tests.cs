using HostPlatform.Transport;

namespace HostPlatform.Tests.Unit;

public sealed class Bell202AndG711Tests
{
    [Fact]
    public void G711_mulaw_codes_round_trip()
    {
        for (var i = 0; i < 256; i++)
        {
            var decoded = G711Codec.MuLawDecode((byte)i);
            var again = G711Codec.MuLawDecode(G711Codec.MuLawEncode(decoded));
            Assert.Equal(decoded, again);
        }
    }

    [Fact]
    public void G711_alaw_codes_round_trip()
    {
        for (var i = 0; i < 256; i++)
        {
            var decoded = G711Codec.ALawDecode((byte)i);
            var again = G711Codec.ALawDecode(G711Codec.ALawEncode(decoded));
            Assert.Equal(decoded, again);
        }
    }

    [Fact]
    public void Bell202_bytes_survive_a_chunked_audio_loop()
    {
        var payload = new byte[] { 0x02, 0x00, 0x41, 0xFF, 0x7F, 0x55, 0x10, 0x03 };
        var tx = new Bell202Link();
        tx.Queue(payload);
        var bits = Bell202Link.LeadingMarkBits + payload.Length * 10 + 24;
        var total = (int)Math.Ceiling(bits * Bell202Link.SamplesPerBit) + 32;
        var rx = new Bell202Link();
        var decoded = new List<byte>();
        var left = total;
        while (left > 0)
        {
            var n = Math.Min(160, left);
            decoded.AddRange(rx.Push(tx.Render(n)));
            left -= n;
        }

        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void Minimodem_decodes_the_same_bell202_wav_when_it_is_installed()
    {
        var bin = MinimodemTool.Find();
        if (bin == null)
            return; // minimodem is optional on developer machines

        var payload = new byte[] { 0x02, 0x41, 0x42, 0x03 };
        var tx = new Bell202Link();
        tx.Queue(payload);
        var bits = Bell202Link.LeadingMarkBits + payload.Length * 10 + 40;
        var samples = tx.Render((int)Math.Ceiling(bits * Bell202Link.SamplesPerBit) + 64);
        var path = Path.Combine(Path.GetTempPath(), "coinline-bell202.wav");
        WritePcm16(path, samples);
        var decoded = MinimodemTool.DecodeWav(path);
        Assert.NotNull(decoded);
        Assert.Equal(payload, decoded);
    }

    private static void WritePcm16(string path, float[] samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataBytes = samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Bell202Link.SampleRate);
        writer.Write(Bell202Link.SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            var scaled = Math.Clamp((int)Math.Round(sample * 32767f), short.MinValue, short.MaxValue);
            writer.Write((short)scaled);
        }
    }
}
