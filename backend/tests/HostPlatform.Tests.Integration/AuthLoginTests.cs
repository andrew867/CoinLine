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
    public async Task Lab_operator_can_sign_in_and_a_wrong_password_is_rejected()
    {
        var denied = await _client.PostAsJsonAsync("/api/auth/login", new { username = "operator", password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        var ok = await _client.PostAsJsonAsync("/api/auth/login", new { username = "operator", password = "lab-operator" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await ok.Content.ReadAsStringAsync(), JsonOpts);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()));
        Assert.Equal("Admin", body.GetProperty("role").GetString());
    }
}
