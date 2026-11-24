namespace HostPlatform.Transport;

/// <summary>
/// G.711 μ-law and A-law. SIP audio uses one of these two sample formats so the
/// waveform is not compressed again before the modem.
/// </summary>
public static class G711Codec
{
    public static short MuLawDecode(byte encoded)
    {
        var u = (byte)~encoded;
        var sign = u & 0x80;
        var exponent = (u >> 4) & 0x07;
        var mantissa = u & 0x0F;
        var sample = ((mantissa << 3) + 0x84) << exponent;
        sample -= 0x84;
        return (short)(sign != 0 ? -sample : sample);
    }

    public static byte MuLawEncode(short sample)
    {
        const int bias = 0x84;
        const int clip = 32635;
        var sign = (sample >> 8) & 0x80;
        if (sign != 0)
            sample = (short)-sample;
        if (sample > clip)
            sample = clip;
        sample += bias;
        var exponent = 7;
        for (var mask = 0x4000; (sample & mask) == 0 && exponent > 0; exponent--, mask >>= 1)
        {
        }

        var mantissa = (sample >> (exponent + 3)) & 0x0F;
        return (byte)~(sign | (exponent << 4) | mantissa);
    }

    public static short ALawDecode(byte encoded)
    {
        var a = (byte)(encoded ^ 0x55);
        var magnitude = (a & 0x0F) << 4;
        var segment = (a & 0x70) >> 4;
        var sample = segment switch
        {
            0 => magnitude + 8,
            1 => magnitude + 0x108,
            _ => (magnitude + 0x108) << (segment - 1)
        };
        return (short)((a & 0x80) != 0 ? sample : -sample);
    }

    public static byte ALawEncode(short sample)
    {
        var mask = 0xD5;
        var pcm = (int)sample;
        if (pcm < 0)
        {
            mask = 0x55;
            pcm = -pcm - 1;
        }

        if (pcm > 32635)
            pcm = 32635;
        var segment = 0;
        for (var end = 0xFF; segment < 8 && pcm > end; segment++)
            end = (end << 1) + 1;
        if (segment >= 8)
            return (byte)(0x7F ^ mask);
        var aval = segment << 4;
        aval |= segment < 2 ? (pcm >> 4) & 0x0F : (pcm >> (segment + 3)) & 0x0F;
        return (byte)(aval ^ mask);
    }

    public static short Decode(byte encoded, bool aLaw) =>
        aLaw ? ALawDecode(encoded) : MuLawDecode(encoded);

    public static byte Encode(short sample, bool aLaw) =>
        aLaw ? ALawEncode(sample) : MuLawEncode(sample);
}
