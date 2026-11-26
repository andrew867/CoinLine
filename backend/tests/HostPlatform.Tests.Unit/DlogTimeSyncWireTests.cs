using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Unit;

public sealed class DlogTimeSyncWireTests
{
    [Fact]
    public void Decodes_exact_binary_utc_calendar_at_wire_second_resolution()
    {
        var time = new DateTime(2026, 11, 26, 7, 44, 27, DateTimeKind.Utc).AddMilliseconds(285);
        var payload = DlogTimeSyncWire.EncodeResponse(time);
        Assert.Equal(new DateTime(2026, 11, 26, 7, 44, 27, DateTimeKind.Utc), DlogTimeSyncWire.DecodeResponse(payload));
    }

    [Fact]
    public void Invalid_or_unrelated_records_do_not_establish_a_clock_offer()
    {
        var payload = DlogTimeSyncWire.EncodeResponse(new DateTime(2026, 11, 26, 7, 44, 27, DateTimeKind.Utc));
        Assert.Null(DlogTimeSyncWire.DecodeResponse([.. payload, 0]));
        Assert.Null(DlogTimeSyncWire.DecodeResponse(payload.AsSpan(0, 7)));
        payload[7] = 0;
        Assert.Null(DlogTimeSyncWire.DecodeResponse(payload));
        payload[2] = 13;
        Assert.Null(DlogTimeSyncWire.DecodeResponse(payload));
    }
}
