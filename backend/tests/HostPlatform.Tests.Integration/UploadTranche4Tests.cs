using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class UploadTranche4Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Ingest_json_array_splits_records_and_operator_review_merges_metadata()
    {
        var payloadUtf8 = Encoding.UTF8.GetBytes("[1,2]");
        var created = await _client.PostAsJsonAsync("/api/uploads", new
        {
            terminalId = (Guid?)null,
            payloadHex = Convert.ToHexString(payloadUtf8),
            metadataJson = "{\"source\":\"itest\"}",
            idempotencyKey = $"idem-{Guid.NewGuid():N}",
            relatedDlogTransactionId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var ingest = await _client.PostAsync($"/api/uploads/{id}/ingest", null);
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);
        var ingestBody = JsonSerializer.Deserialize<JsonElement>(await ingest.Content.ReadAsStringAsync(), JsonOpts);
        Assert.Equal(2, ingestBody.GetProperty("recordCount").GetInt32());
        Assert.Equal("json_array", ingestBody.GetProperty("mode").GetString());

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/uploads/{id}");
        Assert.Equal(2, detail!.GetProperty("records").GetArrayLength());

        var review = await _client.PostAsJsonAsync($"/api/uploads/{id}/operator-review", new { note = "ok for billing" });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var after = await _client.GetFromJsonAsync<JsonElement>($"/api/uploads/{id}");
        Assert.True(after!.GetProperty("decodedMetadataJson").GetString()?.Contains("operatorReview", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Reprocess_requires_confirm()
    {
        var created = await _client.PostAsJsonAsync("/api/uploads", new
        {
            terminalId = (Guid?)null,
            payloadHex = Convert.ToHexString(Encoding.UTF8.GetBytes("[9]")),
            metadataJson = "{}",
            idempotencyKey = $"idem-{Guid.NewGuid():N}",
            relatedDlogTransactionId = (Guid?)null
        });
        var id = JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        await _client.PostAsync($"/api/uploads/{id}/ingest", null);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/uploads/{id}/reprocess", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/uploads/{id}/reprocess?confirm=true", null)).StatusCode);
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }
}
