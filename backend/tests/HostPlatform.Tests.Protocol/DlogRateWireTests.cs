using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Protocol;

public class DlogRateWireTests
{
    [Fact]
    public void Response_is_26_bytes_and_type_64()
    {
        var quote = DlogRateWire.QuoteFromDecision(true, false, 0.02m, 0xA5);
        var raw = DlogRateWire.EncodeResponse(quote);
        Assert.Equal(DlogRateWire.ResponseLength, raw.Length);
        Assert.Equal(DlogRateWire.MessageTypeResponse, raw[0]);
        Assert.Equal(6, raw[1]);
        Assert.Equal(60, raw[2]);
        Assert.Equal(0, raw[3]);
        Assert.Equal(2, raw[4]);
        Assert.Equal(0, raw[5]);
    }

    [Theory]
    [InlineData(0x00, 1)]
    [InlineData(0xA4, 5)]
    [InlineData(0xA5, 6)]
    public void Allowed_quote_uses_the_request_call_type(byte callType, byte expectedRateInfo)
    {
        var quote = DlogRateWire.QuoteFromDecision(true, false, 0.05m, callType);
        Assert.Equal(expectedRateInfo, quote.RateInfo);
    }

    [Fact]
    public void Request_reads_bcd_not_the_longest_ascii_run()
    {
        var noise = "9999999999"u8.ToArray();
        var raw = DlogRateWire.EncodeRequest("5551212", international: true, noise);
        Assert.Equal(DlogRateWire.RequestLength, raw.Length);
        Assert.Equal(DlogRateWire.MessageTypeRequest, raw[0]);
        Assert.True(DlogRateWire.TryParseRequest(raw, out var request));
        Assert.Equal("5551212", request.DialedDigits);
        Assert.True(request.International);
        Assert.NotEqual("9999999999", request.DialedDigits);
    }

    [Fact]
    public void Denied_quote_uses_not_found_or_blocked()
    {
        var unknown = DlogRateWire.QuoteFromDecision(false, false, 0);
        var blocked = DlogRateWire.QuoteFromDecision(false, true, 0);
        Assert.Equal(3, unknown.RateInfo);
        Assert.Equal(4, blocked.RateInfo);
        Assert.Equal(0, unknown.InitialChargeCents);
        Assert.Equal(DlogRateWire.MessageTypeResponse, DlogRateWire.EncodeResponse(blocked)[0]);
    }
}
