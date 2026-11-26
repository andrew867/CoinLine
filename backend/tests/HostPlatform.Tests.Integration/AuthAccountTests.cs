using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class AuthAccountTests(ApiFixture factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task An_admin_can_add_a_person_and_that_person_can_change_password()
    {
        var username = "desk-" + Guid.NewGuid().ToString("N")[..8];
        var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);

        var created = await client.PostAsJsonAsync("/api/auth/accounts", new
        {
            username,
            displayName = "Front desk",
            password = "desk-pass-1",
            role = "Operator"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password = "desk-pass-1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var asDesk = factory.CreateClient();
        asDesk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        var wrong = await asDesk.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = "nope-nope",
            newPassword = "desk-pass-2"
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var changed = await asDesk.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = "desk-pass-1",
            newPassword = "desk-pass-2"
        });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        using (var revoked = await asDesk.GetAsync("/api/auth/me"))
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        var oldLogin = await client.PostAsJsonAsync("/api/auth/login", new { username, password = "desk-pass-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var newLogin = await client.PostAsJsonAsync("/api/auth/login", new { username, password = "desk-pass-2" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        asDesk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await newLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());

        var me = await asDesk.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await me.Content.ReadAsStringAsync(), Json);
        Assert.Equal("Front desk", body.GetProperty("displayName").GetString());
        Assert.Equal("Operator", body.GetProperty("role").GetString());
    }
}
