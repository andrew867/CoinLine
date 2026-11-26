using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class AuthLoginTests(ApiFixture factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Named_fixture_operator_can_sign_in_and_a_wrong_password_is_rejected()
    {
        const string username = "fixture-login";
        TestOperatorAuthentication.EnsureAccount(factory, username, password: TestOperatorAuthentication.FixturePassword);
        var denied = await _client.PostAsJsonAsync("/api/auth/login", new { username, password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        var ok = await _client.PostAsJsonAsync("/api/auth/login", new { username, password = TestOperatorAuthentication.FixturePassword });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await ok.Content.ReadAsStringAsync(), JsonOpts);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()));
        Assert.Equal("Admin", body.GetProperty("role").GetString());
    }
}
