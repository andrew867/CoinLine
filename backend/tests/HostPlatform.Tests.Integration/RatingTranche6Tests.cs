using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class RatingTranche6Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Tariff_catalog_put_delete_and_blocked_delete_when_referenced()
    {
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var create = await _client.PostAsJsonAsync("/api/rate-plans", new
        {
            name = $"Catalog plan {Guid.NewGuid():N}",
            customerId = cust,
            mode = RatingMode.RealTimeRated
        });
        var planId = JsonSerializer.Deserialize<JsonElement>(await create.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var ver = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions", new { cloneFromVersionId = (Guid?)null });
        var verId = JsonSerializer.Deserialize<JsonElement>(await ver.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var t1 = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions/{verId}/tariffs",
            new { name = "A", ratePerMinuteUsd = 0.1m, notes = "n1" });
        var tid1 = JsonSerializer.Deserialize<JsonElement>(await t1.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var t2 = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions/{verId}/tariffs",
            new { name = "B", ratePerMinuteUsd = 0.2m, notes = "" });
        var tid2 = JsonSerializer.Deserialize<JsonElement>(await t2.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var put = await _client.PutAsJsonAsync($"/api/tariffs/{tid1}", new { name = "A2", ratePerMinuteUsd = 0.11m, notes = "x" });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions/{verId}/destination-prefixes",
            new { prefixDigits = "999", tariffId = tid1, notes = "" });

        var delBlocked = await _client.DeleteAsync($"/api/tariffs/{tid1}");
        Assert.Equal(HttpStatusCode.BadRequest, delBlocked.StatusCode);

        var delOk = await _client.DeleteAsync($"/api/tariffs/{tid2}");
        Assert.Equal(HttpStatusCode.NoContent, delOk.StatusCode);
    }

    [Fact]
    public async Task Clone_version_copies_tariffs_prefixes_and_bands()
    {
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var create = await _client.PostAsJsonAsync("/api/rate-plans", new
        {
            name = $"Clone cat {Guid.NewGuid():N}",
            customerId = cust,
            mode = RatingMode.RealTimeRated
        });
        var planId = JsonSerializer.Deserialize<JsonElement>(await create.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var v1r = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions", new { cloneFromVersionId = (Guid?)null });
        var v1 = JsonSerializer.Deserialize<JsonElement>(await v1r.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var tr = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions/{v1}/tariffs",
            new { name = "Peak", ratePerMinuteUsd = 0.5m, notes = "" });
        var tariffId = JsonSerializer.Deserialize<JsonElement>(await tr.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions/{v1}/destination-prefixes",
            new { prefixDigits = "1206", tariffId, notes = "ld" });
        await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions/{v1}/time-bands",
            new { dayOfWeekMask = 127, startMinuteOfDay = 600, endMinuteOfDay = 720, tariffId });

        var v2r = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions", new { cloneFromVersionId = v1 });
        var v2 = JsonSerializer.Deserialize<JsonElement>(await v2r.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var doc = (await _client.GetFromJsonAsync<JsonElement>($"/api/rate-plans/{planId}/versions/{v2}"))!;
        Assert.Equal(1, doc.GetProperty("tariffs").GetArrayLength());
        Assert.Equal(1, doc.GetProperty("destinationPrefixes").GetArrayLength());
        Assert.Equal(1, doc.GetProperty("timeBands").GetArrayLength());
        Assert.Equal("1206", doc.GetProperty("destinationPrefixes")[0].GetProperty("prefixDigits").GetString());
        var newTid = doc.GetProperty("tariffs")[0].GetProperty("id").GetGuid();
        Assert.NotEqual(tariffId, newTid);
        Assert.Equal(newTid, doc.GetProperty("destinationPrefixes")[0].GetProperty("tariffId").GetGuid());
    }

    [Fact]
    public async Task Publish_blocks_catalog_edits()
    {
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var create = await _client.PostAsJsonAsync("/api/rate-plans", new
        {
            name = $"Pub block {Guid.NewGuid():N}",
            customerId = cust,
            mode = RatingMode.RealTimeRated
        });
        var planId = JsonSerializer.Deserialize<JsonElement>(await create.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var ver = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions", new { cloneFromVersionId = (Guid?)null });
        var verId = JsonSerializer.Deserialize<JsonElement>(await ver.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var tr = await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/versions/{verId}/tariffs",
            new { name = "T", ratePerMinuteUsd = 0.01m, notes = "" });
        var tid = JsonSerializer.Deserialize<JsonElement>(await tr.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        await _client.PostAsJsonAsync($"/api/rate-plans/{planId}/publish", new { ratePlanVersionId = verId, confirm = true });

        var putBad = await _client.PutAsJsonAsync($"/api/tariffs/{tid}", new { name = "X", ratePerMinuteUsd = 0.02m, notes = "" });
        Assert.Equal(HttpStatusCode.BadRequest, putBad.StatusCode);
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }
}
