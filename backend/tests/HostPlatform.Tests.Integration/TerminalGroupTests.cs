using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class TerminalGroupTests(ApiFixture factory)
{
    private readonly HttpClient _client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task A_new_group_can_be_assigned_to_a_phone()
    {
        var customers = (await _client.GetFromJsonAsync<JsonElement>("/api/customers", Json))!;
        var customerId = customers[0].GetProperty("id").GetGuid();
        var sites = (await _client.GetFromJsonAsync<JsonElement>("/api/sites", Json))!;
        var siteId = sites[0].GetProperty("id").GetGuid();

        var created = await _client.PostAsJsonAsync("/api/terminal-groups", new { name = "Shelter row", customerId });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var group = JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync(), Json);
        var groupId = group.GetProperty("id").GetGuid();

        var phone = await _client.PostAsJsonAsync("/api/terminals", new
        {
            siteId,
            terminalGroupId = (Guid?)null,
            transportEndpointId = (Guid?)null,
            firmwareVersionId = (Guid?)null,
            terminalIdHex = "0A0B0C0D0E",
            displayName = "Group probe",
            status = 1
        });
        Assert.Equal(HttpStatusCode.Created, phone.StatusCode);
        var phoneBody = JsonSerializer.Deserialize<JsonElement>(await phone.Content.ReadAsStringAsync(), Json);
        var phoneId = phoneBody.GetProperty("id").GetGuid();

        var assigned = await _client.PutAsJsonAsync($"/api/terminals/{phoneId}/group", new { terminalGroupId = groupId });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        var list = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals", Json))!;
        var row = list.EnumerateArray().First(t => t.GetProperty("id").GetGuid() == phoneId);
        Assert.Equal("Shelter row", row.GetProperty("groupName").GetString());

        var groups = (await _client.GetFromJsonAsync<JsonElement>("/api/terminal-groups", Json))!;
        var listed = groups.EnumerateArray().First(g => g.GetProperty("id").GetGuid() == groupId);
        Assert.True(listed.GetProperty("terminalCount").GetInt32() >= 1);

        var renamed = await _client.PutAsJsonAsync($"/api/terminal-groups/{groupId}", new { name = "Shelter renamed" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var blocked = await _client.DeleteAsync($"/api/terminal-groups/{groupId}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var cleared = await _client.PutAsJsonAsync($"/api/terminals/{phoneId}/group", new { terminalGroupId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var removed = await _client.DeleteAsync($"/api/terminal-groups/{groupId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }
}
