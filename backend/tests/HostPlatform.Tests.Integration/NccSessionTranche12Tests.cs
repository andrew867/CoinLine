using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class NccSessionTranche12Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Session_lifecycle_status_list_close_archive_dashboard()
    {
        var all = await _client.GetFromJsonAsync<JsonElement>("/api/ncc/sessions?includeArchived=true", JsonOpts);
        Assert.True(all.GetArrayLength() >= 1);

        JsonElement seedRow = default;
        var found = false;
        foreach (var r in all.EnumerateArray())
        {
            if (r.GetProperty("correlationId").GetString() == "ncc-seed")
            {
                seedRow = r;
                found = true;
                break;
            }
        }

        Assert.True(found, "Expected demo seed NCC session correlationId ncc-seed.");
        Assert.True(seedRow.TryGetProperty("status", out var statusProp));
        var sid = seedRow.GetProperty("id").GetGuid();

        var dash0 = await _client.GetFromJsonAsync<JsonElement>("/api/operator/dashboard", JsonOpts);
        Assert.True(dash0.TryGetProperty("openNccSessions", out var openProp));
        // Dashboard open count is socket-bound links only (not leftover Active DB rows).
        Assert.True(openProp.GetInt32() >= 0);

        // Active seed → close (demo seed is normally Closed; keep path for leftover Active rows)
        if (statusProp.GetInt32() == (int)NccSessionStatus.Active)
        {
            var closed = await _client.PostAsync($"/api/ncc/sessions/{sid}/close", null);
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            var closedBody = await closed.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            Assert.Equal((int)NccSessionStatus.Closed, closedBody!.GetProperty("status").GetInt32());
        }

        var archResp = await _client.PostAsync($"/api/ncc/sessions/{sid}/archive", null);
        Assert.Equal(HttpStatusCode.OK, archResp.StatusCode);

        var defaultList = await _client.GetFromJsonAsync<JsonElement>("/api/ncc/sessions", JsonOpts);
        Assert.DoesNotContain(defaultList!.EnumerateArray(), r => r.GetProperty("id").GetGuid() == sid);

        var again = await _client.GetFromJsonAsync<JsonElement>("/api/ncc/sessions?includeArchived=true", JsonOpts);
        Assert.True(again.GetArrayLength() >= 1);
        JsonElement? ours = null;
        foreach (var r in again.EnumerateArray())
        {
            if (r.GetProperty("id").GetGuid() != sid)
                continue;
            ours = r;
            break;
        }

        Assert.True(ours.HasValue);
        Assert.Equal((int)NccSessionStatus.Archived, ours!.Value.GetProperty("status").GetInt32());
        Assert.True(ours.Value.TryGetProperty("socketBound", out var bound));
        Assert.False(bound.GetBoolean());
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = TestOperatorAuthentication.CreateAuthenticatedClient(f, "itest@local", "Admin");
        return c;
    }
}
