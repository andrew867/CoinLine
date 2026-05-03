using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class OperatorTranche9Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Operator_dashboard_and_search_and_console_timelines_smoke()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/operator/dashboard")).StatusCode);

        var search = await _client.GetFromJsonAsync<JsonElement>("/api/operator/search?q=sample&limit=5", JsonOpts);
        Assert.True(search.TryGetProperty("customers", out _));

        var custId = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/operator/customers/{custId}/console")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/operator/customers/{custId}/timeline")).StatusCode);

        var termId = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))![0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/operator/terminals/{termId}/timeline")).StatusCode);
    }

    [Fact]
    public async Task Audit_events_supports_paging_and_filters()
    {
        var paged = await _client.GetFromJsonAsync<JsonElement>("/api/audit/events?page=1&pageSize=5", JsonOpts);
        Assert.True(paged.TryGetProperty("items", out var items) && items.GetArrayLength() <= 5);

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/audit/events?q=rating")).StatusCode);
    }

    [Fact]
    public async Task Terminals_list_legacy_and_paged_shape()
    {
        var legacy = await _client.GetFromJsonAsync<JsonElement>("/api/terminals");
        Assert.Equal(JsonValueKind.Array, legacy!.ValueKind);

        var paged = await _client.GetFromJsonAsync<JsonElement>("/api/terminals?page=1&pageSize=3", JsonOpts);
        Assert.True(paged.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task Card_account_timeline_smoke()
    {
        var accounts = await _client.GetFromJsonAsync<JsonElement>("/api/cards/accounts");
        var id = accounts![0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/cards/accounts/{id}/timeline")).StatusCode);
    }

    private static HttpClient CreateClient(ApiFixture factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Operator-Id", "itest@local");
        client.DefaultRequestHeaders.Add("X-Operator-Role", "Admin");
        return client;
    }
}
