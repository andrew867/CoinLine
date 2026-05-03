using HostPlatform.Domain;

namespace HostPlatform.Tests.Unit;

/// <summary>Regression: persistence must retain raw bytes for unknown / future decoders.</summary>
public sealed class DlogTransactionPreservationTests
{
    [Fact]
    public void Entity_retains_raw_payload_reference()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var t = new DlogTransaction
        {
            MessageType = 999,
            MessageTypeName = "UNKNOWN",
            RawPayload = bytes,
            IsUnknownMessageType = true,
            CapturedAtUtc = DateTime.UtcNow
        };
        Assert.Equal(bytes, t.RawPayload);
        Assert.True(t.IsUnknownMessageType);
    }
}
