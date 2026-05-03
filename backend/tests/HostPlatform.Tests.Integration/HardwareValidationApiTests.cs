using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class HardwareValidationApiTests(ApiFixture factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Checklists_and_evidence_guide_expose_doc_paths()
    {
        var c = await _client.GetFromJsonAsync<JsonElement>("/api/hw-validation/checklists", JsonOpts);
        Assert.Equal(JsonValueKind.Array, c.GetProperty("repositoryPaths").ValueKind);
        var g = await _client.GetFromJsonAsync<JsonElement>("/api/hw-validation/evidence-guide", JsonOpts);
        Assert.Contains("attaching-evidence.md", g.GetProperty("primaryDoc").GetString() ?? "");
        Assert.True(g.GetProperty("hardwareValidationRequired").GetBoolean());
    }

    [Fact]
    public async Task Import_replay_and_duplicate_checksum()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "hw_validation", "sample_session_envelope_v1.json");
        Assert.True(File.Exists(path), path);
        var json = await File.ReadAllTextAsync(path);
        var first = await _client.PostAsync("/api/hw-validation/captured-sessions",
            new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var body1 = await first.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var id = body1.GetProperty("id").GetGuid();
        Assert.True(body1.GetProperty("hardwareValidationRequired").GetBoolean());

        var second = await _client.PostAsync("/api/hw-validation/captured-sessions",
            new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body2 = await second.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.True(body2.GetProperty("duplicate").GetBoolean());
        Assert.Equal(id, body2.GetProperty("id").GetGuid());

        var replay = await _client.PostAsync($"/api/hw-validation/captured-sessions/{id}/replay",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayJson = await replay.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.True(replayJson.GetProperty("globalHardwareValidationRequired").GetBoolean());
        Assert.True(replayJson.TryGetProperty("segments", out var segs));
        Assert.True(segs.GetArrayLength() > 0);

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/hw-validation/captured-sessions", JsonOpts);
        Assert.True(list.GetArrayLength() >= 1);
    }
}
