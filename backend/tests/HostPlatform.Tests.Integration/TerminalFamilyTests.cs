using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class TerminalFamilyTests(ApiFixture factory)
{
    private readonly HttpClient _client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);

    [Fact]
    public async Task Terminal_family_can_be_selected_filtered_changed_and_cleared_independently_of_firmware()
    {
        var catalogue = await _client.GetFromJsonAsync<JsonElement>("/api/terminals/families");
        string[] type45 = catalogue.EnumerateArray()
            .Where(row => row.GetProperty("maximizerTypeNumbers").EnumerateArray().Any(code => code.GetInt32() == 45))
            .Select(row => row.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(new[] { "mtr111-desk", "mtr111-coin-basic" }, type45);

        var sites = await _client.GetFromJsonAsync<JsonElement>("/api/sites");
        var versions = await _client.GetFromJsonAsync<JsonElement>("/api/firmware/versions");
        var firmwareVersionId = versions[0].GetProperty("id").GetGuid();
        using var created = await _client.PostAsJsonAsync("/api/terminals", new
        {
            siteId = sites[0].GetProperty("id").GetGuid(),
            firmwareVersionId,
            terminalIdHex = "4E2E000001",
            displayName = "Family catalogue test",
            status = 1,
            terminalFamilyId = "mtr19-multipay"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var phone = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = phone.GetProperty("id").GetGuid();
        var path = $"/api/terminals/{id}";

        var matching = await _client.GetFromJsonAsync<JsonElement>("/api/terminals?terminalFamilyId=mtr19-multipay&page=1");
        var row = Assert.Single(matching.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
        Assert.Equal("MTR 1.9 Multi-pay", row.GetProperty("terminalFamilyLabel").GetString());

        using var set = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Family preview test", isDefault = false });
        Assert.Equal(HttpStatusCode.Created, set.StatusCode);
        var setBody = await set.Content.ReadFromJsonAsync<JsonElement>();
        var preview = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/tables/sets/{setBody.GetProperty("id").GetGuid()}/assignment-preview?targetType=terminal&targetId={id}");
        var target = Assert.Single(preview.GetProperty("targets").EnumerateArray());
        Assert.Equal("MTR 1.9 Multi-pay", target.GetProperty("terminalFamilyLabel").GetString());

        using var invalid = await _client.PutAsJsonAsync($"{path}/family", new { terminalFamilyId = "2" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var changed = await _client.PutAsJsonAsync($"{path}/family", new { terminalFamilyId = "mtr111-coin-basic" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        // Older inventory clients can still update the phone without sending the family.
        using var update = await _client.PutAsJsonAsync(path, new
        {
            displayName = "Family catalogue test updated",
            terminalIdHex = "4E2E000001",
            firmwareVersionId,
            status = 1
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var detail = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("mtr111-coin-basic", detail.GetProperty("terminalFamilyId").GetString());
        Assert.Equal(firmwareVersionId, detail.GetProperty("firmwareVersionId").GetGuid());

        using var cleared = await _client.PutAsJsonAsync($"{path}/family", new { terminalFamilyId = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        detail = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("terminalFamilyId").ValueKind);
    }
}
