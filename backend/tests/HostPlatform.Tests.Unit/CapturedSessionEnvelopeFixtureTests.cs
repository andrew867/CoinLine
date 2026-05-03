using System.Text.Json;

namespace HostPlatform.Tests.Unit;

/// <summary>
/// Ensures the shared CI envelope used by import/replay tests stays aligned with promote-session docs.
/// </summary>
public sealed class CapturedSessionEnvelopeFixtureTests
{
    private static string SampleEnvelopePath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "hw_validation", "sample_session_envelope_v1.json");

    [Fact]
    public void Sample_session_envelope_has_schema_v1_and_segment_matrix()
    {
        Assert.True(File.Exists(SampleEnvelopePath), SampleEnvelopePath);
        using var doc = JsonDocument.Parse(File.ReadAllText(SampleEnvelopePath));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var types = root.GetProperty("evidence").GetProperty("segments").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString())
            .ToArray();
        Assert.Contains("ncc_uart_hex", types);
        Assert.Contains("dlog_payload_hex", types);
        Assert.Contains("table_opaque_hex", types);
        Assert.Contains("rated_call_evidence", types);
        Assert.Contains("modem_trace_evidence", types);
        Assert.Contains("card_transaction_evidence", types);
        Assert.Contains("firmware_dry_run_evidence", types);
    }
}
