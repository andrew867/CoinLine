namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class SmokeTests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);

    [Fact]
    public async Task Health_ok()
    {
        var r = await _client.GetAsync("/health");
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Health_live_ok()
    {
        var r = await _client.GetAsync("/health/live");
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Ready_ok()
    {
        var r = await _client.GetAsync("/ready");
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Health_ready_ok()
    {
        var r = await _client.GetAsync("/health/ready");
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Metrics_prometheus_ok()
    {
        var r = await _client.GetAsync("/metrics");
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains('#', body);
    }

    [Fact]
    public async Task Customers_seed_present()
    {
        var r = await _client.GetAsync("/api/customers");
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("Sample Transit Co", body);
    }

    [Fact]
    public async Task Swagger_openapi_json_ok()
    {
        var r = await _client.GetAsync("/swagger/v1/swagger.json");
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"openapi\"", body);
        Assert.Contains("/api/cards", body);
        Assert.Contains("/api/craft", body);
        Assert.Contains("/api/craft/commands", body);
        Assert.Contains("/api/firmware", body);
        Assert.Contains("/api/firmware/packages", body);
        Assert.Contains("/api/firmware/jobs", body);
        Assert.Contains("/api/operator", body);
    }

    [Fact]
    public async Task Card_products_seed_present()
    {
        var r = await _client.GetAsync("/api/cards/products");
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("MAG", body);
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }
}
