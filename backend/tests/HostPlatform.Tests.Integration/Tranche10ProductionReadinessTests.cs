using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class Tranche10ProductionReadinessTests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);

    [Fact]
    public async Task Health_live_ok()
    {
        var r = await _client.GetAsync("/health/live");
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Metrics_prometheus_text_ok()
    {
        var r = await _client.GetAsync("/metrics");
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("#", body);
    }

    [Fact]
    public async Task Ready_delegates_to_health_checks()
    {
        var r = await _client.GetAsync("/ready");
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Download_batch_client_idempotency_returns_same_batch()
    {
        var sets = await _client.GetFromJsonAsync<JsonElement>("/api/tables/sets");
        var publishedId = Guid.Empty;
        foreach (var s in sets!.EnumerateArray())
        {
            if (s.GetProperty("status").GetInt32() == 1)
            {
                publishedId = s.GetProperty("id").GetGuid();
                break;
            }
        }

        Assert.NotEqual(Guid.Empty, publishedId);
        var tid = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))!.EnumerateArray().First()
            .GetProperty("id").GetGuid();

        var idem = $"idem-{Guid.NewGuid():N}";
        var body = new { tableSetId = publishedId, scope = "Full", clientIdempotencyKey = idem };
        var first = await _client.PostAsJsonAsync($"/api/terminals/{tid}/downloads", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await _client.PostAsJsonAsync($"/api/terminals/{tid}/downloads", body);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var j1 = JsonSerializer.Deserialize<JsonElement>(await first.Content.ReadAsStringAsync());
        var j2 = JsonSerializer.Deserialize<JsonElement>(await second.Content.ReadAsStringAsync());
        Assert.Equal(j1.GetProperty("id").GetGuid(), j2.GetProperty("id").GetGuid());
        Assert.True(j2.TryGetProperty("wasExisting", out var we) && we.GetBoolean());
    }

    [Fact]
    public async Task Dlog_ingest_burst_survives()
    {
        for (var i = 0; i < 12; i++)
        {
            var ingest = await _client.PostAsJsonAsync("/api/dlog/transactions/ingest", new
            {
                rawPayloadHex = "01AB",
                terminalId = (Guid?)null,
                nccSessionId = (Guid?)null,
                sessionCorrelationId = "tranche10-burst",
                messageType = (int?)null,
                firstByteIsMessageType = (bool?)true,
                direction = 1,
                clientIdempotencyKey = $"burst-{i}-{Guid.NewGuid():N}",
                capturedAtUtc = (DateTime?)null
            });
            ingest.EnsureSuccessStatusCode();
        }
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }
}
