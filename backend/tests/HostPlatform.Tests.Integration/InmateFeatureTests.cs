using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class InmateFeatureTests(ApiFixture factory)
{
    private readonly HttpClient _client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);

    [Fact]
    public async Task Named_configurations_save_copy_update_and_delete_with_revision_checks()
    {
        var schema = await _client.GetFromJsonAsync<JsonElement>("/api/inmate-features/schema");
        Assert.Equal(7, schema.GetProperty("sections").GetArrayLength());
        var settings = new { pinStrategy = 1, pinLength = 20, recorderPresent = true, recordCode = "a#", repeatInterval = 1800 };
        using var created = await _client.PostAsJsonAsync("/api/inmate-features", new { name = "FTR001", description = "Editor test", settings });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = draft.GetProperty("id").GetGuid();
        var path = $"/api/inmate-features/{id}";
        var revision = draft.GetProperty("revision").GetInt32();
        var saved = draft.GetProperty("settings");
        Assert.Equal("A#", saved.GetProperty("recordCode").GetString());
        Assert.Equal(60, saved.GetProperty("firstWarning").GetInt32());
        Assert.True(saved.GetProperty("recorderPresent").GetBoolean());

        using var duplicate = await _client.PostAsJsonAsync("/api/inmate-features", new { name = "ftr001" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var copy = await _client.PostAsJsonAsync("/api/inmate-features", new { name = "FTR002", settings = saved });
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);

        var update = new { name = "FTR001", description = "Updated", settings = new { pinLength = 8 }, revision };
        using var updated = await _client.PutAsJsonAsync(path, update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var stale = await _client.PutAsJsonAsync(path, update);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var detail = await _client.GetFromJsonAsync<JsonElement>(path);
        var edited = detail.GetProperty("settings");
        Assert.Equal(8, edited.GetProperty("pinLength").GetInt32());
        Assert.Equal(1800, edited.GetProperty("repeatInterval").GetInt32());
        Assert.Equal("A#", edited.GetProperty("recordCode").GetString());
        Assert.Equal("Updated", detail.GetProperty("description").GetString());
        var currentRevision = detail.GetProperty("revision").GetInt32();

        using var unconfirmed = await _client.DeleteAsync($"{path}?revision={currentRevision}");
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        using var staleDelete = await _client.DeleteAsync($"{path}?confirm=true&revision={revision}");
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
        using var deleted = await _client.DeleteAsync($"{path}?confirm=true&revision={currentRevision}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/inmate-features");
        Assert.Contains(list.EnumerateArray(), row => row.GetProperty("name").GetString() == "FTR002");
    }

    [Fact]
    public async Task Invalid_settings_return_field_errors()
    {
        using var response = await _client.PostAsJsonAsync("/api/inmate-features", new
        {
            name = "TOOLONG",
            settings = new { pinLength = 5, firstWarning = 19, secondWarning = 10, recordCode = "E?",
                collectRecording = 4, collectKeypad = 301, recorderPresent = 1, unlistedField = true }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors");
        foreach (var key in new[] { "name", "settings.pinLength", "settings.firstWarning", "settings.recordCode",
            "settings.collectRecording", "settings.collectKeypad", "settings.recorderPresent", "settings.unlistedField" })
            Assert.True(errors.TryGetProperty(key, out _), $"Expected an error on {key}.");
    }
}
