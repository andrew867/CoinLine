using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Protocol;

public sealed class DlogImmediateRequestDiagnosticsTests
{
    [Fact]
    public void Diagnostic_only_reads_recognized_record_boundaries()
    {
        byte[] request = new byte[39]; request[0] = 2;
        byte[] combined = new byte[54]; combined[0] = 53; combined[1] = 61;
        byte[] legacy = new byte[35]; legacy[0] = 4;
        byte[] truncated = [61, 0xAB];
        byte[] data = [..request, ..combined, ..legacy, ..truncated];
        Assert.False(DlogImmediateRequestWire.TrySplit(data, out var length, out _));
        Assert.Equal(new byte[] { 53, 4, 61 }, DlogImmediateRequestWire.RecognizedSuffixTypes(data, length));
        Assert.Empty(DlogImmediateRequestWire.RecognizedSuffixTypes([..request, 0xAB, 53], request.Length));
        Assert.Empty(DlogImmediateRequestWire.RecognizedSuffixTypes([2, 53], request.Length));
    }
}
