using System.Buffers.Binary;

namespace HostPlatform.Protocols.Dlog;

/// <summary>
/// Type 63 rate request and type 64 rate response on the NCC payload.
/// The dialed number is 10 BCD bytes, high nibble first. A nibble above 9 ends the number.
/// Sixteen-bit periods and charges are little-endian and packed on byte boundaries.
/// Charges are cents. A type 64 payload is 26 bytes: message type, a 19-byte quote, and 6 spare bytes.
/// The quote flag is 1 for a local rate, 3 when a rate is unavailable, 4 when the number is blocked,
/// and 5 or 6 for an intra-area or inter-area toll rate.
/// </summary>
public static class DlogRateWire
{
    public const int MessageTypeRequest = 63;
    public const int MessageTypeResponse = 64;
    public const int NumberLength = 10;
    public const int RequestLength = 25;
    public const int ResponseLength = 26;

    public const byte RateFound = 1;
    public const byte RateNotFound = 3;
    public const byte RateBlocked = 4;
    private const byte RateFoundIntraToll = 5;
    private const byte RateFoundInterToll = 6;

    public readonly record struct RateRequest(
        string DialedDigits,
        bool International,
        byte CallingTelco,
        byte Carrier,
        byte CallType);

    public readonly record struct RateQuote(
        byte RateInfo,
        ushort InitialPeriodSeconds,
        ushort InitialChargeCents,
        ushort InitialDurationSeconds,
        ushort AdditionalPeriodSeconds,
        ushort AdditionalChargeCents,
        ushort AdditionalDurationSeconds);

    public static bool TryParseRequest(ReadOnlySpan<byte> data, out RateRequest request)
    {
        request = default;
        if (data.Length < 3 + NumberLength || data[0] != MessageTypeRequest)
            return false;

        request = new RateRequest(
            DecodeBcd(data.Slice(3, NumberLength)),
            data[2] != 0,
            data[1],
            data.Length > 19 ? data[19] : (byte)0,
            data.Length > 20 ? data[20] : (byte)0);
        return true;
    }

    public static string DecodeBcd(ReadOnlySpan<byte> bcd)
    {
        var chars = new char[bcd.Length * 2];
        var n = 0;
        foreach (var b in bcd)
        {
            var hi = (b >> 4) & 0x0F;
            var lo = b & 0x0F;
            if (hi > 9)
                break;
            chars[n++] = (char)('0' + hi);
            if (lo > 9)
                break;
            chars[n++] = (char)('0' + lo);
        }

        return new string(chars, 0, n);
    }

    public static byte[] EncodeRequest(string digits, bool international, ReadOnlySpan<byte> trailingNoise = default)
    {
        var buf = new byte[RequestLength];
        buf[0] = MessageTypeRequest;
        buf[2] = international ? (byte)1 : (byte)0;
        EncodeBcd(digits, buf.AsSpan(3, NumberLength));
        if (!trailingNoise.IsEmpty)
        {
            var room = buf.Length - (3 + NumberLength);
            var take = Math.Min(room, trailingNoise.Length);
            trailingNoise[..take].CopyTo(buf.AsSpan(3 + NumberLength));
        }

        return buf;
    }

    public static byte[] EncodeResponse(RateQuote quote)
    {
        var buf = new byte[ResponseLength];
        buf[0] = MessageTypeResponse;
        buf[1] = quote.RateInfo;
        WriteU16(buf, 2, quote.InitialPeriodSeconds);
        WriteU16(buf, 4, quote.InitialChargeCents);
        WriteU16(buf, 6, quote.InitialDurationSeconds);
        WriteU16(buf, 14, quote.AdditionalPeriodSeconds);
        WriteU16(buf, 16, quote.AdditionalChargeCents);
        WriteU16(buf, 18, quote.AdditionalDurationSeconds);
        return buf;
    }

    public static RateQuote QuoteFromDecision(bool allowed, bool blocked, decimal amountUsd, byte callType = 0)
    {
        var cents = (int)Math.Round(amountUsd * 100m, MidpointRounding.AwayFromZero);
        if (cents < 0)
            cents = 0;
        if (cents > ushort.MaxValue)
            cents = ushort.MaxValue;
        var charge = (ushort)cents;
        var period = charge > 0 ? (ushort)60 : (ushort)0;
        var foundRateInfo = (callType & 0x0F) switch
        {
            4 => RateFoundIntraToll,
            5 => RateFoundInterToll,
            _ => RateFound
        };
        var info = allowed ? foundRateInfo : blocked ? RateBlocked : RateNotFound;
        return new RateQuote(info, period, charge, period, period, charge, period);
    }

    private static void EncodeBcd(string digits, Span<byte> dest)
    {
        dest.Fill(0xFF);
        var i = 0;
        foreach (var c in digits)
        {
            if (c is < '0' or > '9')
                continue;
            var digit = c - '0';
            var index = i / 2;
            if (index >= dest.Length)
                break;
            if ((i & 1) == 0)
                dest[index] = (byte)((digit << 4) | 0x0F);
            else
                dest[index] = (byte)((dest[index] & 0xF0) | digit);
            i++;
        }
    }

    private static void WriteU16(byte[] buf, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset), value);
}
