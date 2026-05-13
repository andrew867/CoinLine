using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class DlogTranche9Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Correlation_pairs_route_matches_rules_count()
    {
        var j = await _client.GetFromJsonAsync<JsonElement>("/api/dlog/correlation-pairs", JsonOpts);
        Assert.Equal(
            HostPlatform.Protocols.Dlog.DlogCorrelationRules.CompatibilityPairs.Count,
            j.GetArrayLength());
    }

    [Fact]
    public async Task Response_first_ingest_then_request_still_links()
    {
        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals", JsonOpts))![0].GetProperty("id").GetGuid();
        var session = $"t9-{Guid.NewGuid():N}";
        var ts = DateTime.UtcNow.AddSeconds(-2);
        var resp = await _client.PostAsJsonAsync("/api/dlog/transactions/ingest", new
        {
            rawPayloadHex = "4002",
            terminalId = term,
            nccSessionId = (Guid?)null,
            sessionCorrelationId = session,
            messageType = (int?)null,
            firstByteIsMessageType = true,
            direction = (int)DlogDirection.TerminalToHost,
            clientIdempotencyKey = $"resp-first-{Guid.NewGuid():N}",
            capturedAtUtc = ts
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var req = await _client.PostAsJsonAsync("/api/dlog/transactions/ingest", new
        {
            rawPayloadHex = "3F01",
            terminalId = term,
            nccSessionId = (Guid?)null,
            sessionCorrelationId = session,
            messageType = (int?)null,
            firstByteIsMessageType = true,
            direction = (int)DlogDirection.TerminalToHost,
            clientIdempotencyKey = $"req-second-{Guid.NewGuid():N}",
            capturedAtUtc = ts.AddMilliseconds(500)
        });
        Assert.Equal(HttpStatusCode.Created, req.StatusCode);
        var reqId = JsonSerializer.Deserialize<JsonElement>(await req.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions/{reqId}", JsonOpts);
        Assert.True(detail!.GetProperty("correlationLinks").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Maint_request_response_pairs_when_ordered()
    {
        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals", JsonOpts))![0].GetProperty("id").GetGuid();
        var session = $"maint-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/dlog/transactions/ingest", new
        {
            rawPayloadHex = "0600",
            terminalId = term,
            sessionCorrelationId = session,
            messageType = (int?)null,
            firstByteIsMessageType = true,
            direction = (int)DlogDirection.TerminalToHost,
            clientIdempotencyKey = $"mreq-{Guid.NewGuid():N}",
            capturedAtUtc = (DateTime?)null
        });

        var ack = await _client.PostAsJsonAsync("/api/dlog/transactions/ingest", new
        {
            rawPayloadHex = "0F00",
            terminalId = term,
            sessionCorrelationId = session,
            messageType = (int?)null,
            firstByteIsMessageType = true,
            direction = (int)DlogDirection.TerminalToHost,
            clientIdempotencyKey = $"mack-{Guid.NewGuid():N}",
            capturedAtUtc = (DateTime?)null
        });
        Assert.Equal(HttpStatusCode.Created, ack.StatusCode);
        var ackId = JsonSerializer.Deserialize<JsonElement>(await ack.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions/{ackId}", JsonOpts);
        Assert.True(detail!.GetProperty("correlationLinks").GetArrayLength() >= 1);
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }
}
