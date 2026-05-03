using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class CraftTranche7Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Craft_session_command_defer_simulate_cancel_and_audit()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/craft/command-types")).StatusCode);

        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals", JsonOpts))![0].GetProperty("id").GetGuid();
        var s = await _client.PostAsJsonAsync("/api/craft/sessions",
            new { terminalId = term, technicianId = "tech-itest", operatorId = "op-itest", fieldNotes = "tranche7" });
        Assert.Equal(HttpStatusCode.Created, s.StatusCode);
        var sid = JsonSerializer.Deserialize<JsonElement>(await s.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var detail0 = await _client.GetFromJsonAsync<JsonElement>($"/api/craft/sessions/{sid}", JsonOpts);
        Assert.True(detail0!.TryGetProperty("hardwareValidationNotice", out var hw));
        Assert.Contains("HARDWARE_VALIDATION_REQUIRED", hw.GetString());
        Assert.True(detail0.TryGetProperty("craftAuditTrail", out var trail0));
        Assert.True(trail0.GetArrayLength() >= 1);

        var deferCmd = await _client.PostAsJsonAsync($"/api/craft/sessions/{sid}/commands", new
        {
            commandName = "ping",
            requestHex = "00",
            commandTypeCode = "ping",
            deferSimulation = true
        });
        Assert.Equal(HttpStatusCode.OK, deferCmd.StatusCode);
        var cmdEl = JsonSerializer.Deserialize<JsonElement>(await deferCmd.Content.ReadAsStringAsync(), JsonOpts);
        var cmdId = cmdEl.GetProperty("id").GetGuid();
        Assert.Equal((int)CraftCommandStatus.Queued, cmdEl.GetProperty("status").GetInt32());

        var getCmd = await _client.GetFromJsonAsync<JsonElement>($"/api/craft/commands/{cmdId}", JsonOpts);
        Assert.Equal((int)CraftCommandStatus.Queued, getCmd!.GetProperty("status").GetInt32());

        var sim = await _client.PostAsJsonAsync($"/api/craft/commands/{cmdId}/simulate", new { });
        Assert.Equal(HttpStatusCode.OK, sim.StatusCode);
        var afterSim = await _client.GetFromJsonAsync<JsonElement>($"/api/craft/commands/{cmdId}", JsonOpts);
        Assert.Equal((int)CraftCommandStatus.Succeeded, afterSim!.GetProperty("status").GetInt32());

        var defer2 = await _client.PostAsJsonAsync($"/api/craft/sessions/{sid}/commands", new
        {
            commandName = "ping",
            requestHex = "01",
            deferSimulation = true
        });
        var cmd2Id = JsonSerializer.Deserialize<JsonElement>(await defer2.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var cancel = await _client.PostAsJsonAsync($"/api/craft/commands/{cmd2Id}/cancel", new { reason = "test_cancel" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelled = await _client.GetFromJsonAsync<JsonElement>($"/api/craft/commands/{cmd2Id}", JsonOpts);
        Assert.Equal((int)CraftCommandStatus.Cancelled, cancelled!.GetProperty("status").GetInt32());

        var auto = await _client.PostAsJsonAsync($"/api/craft/sessions/{sid}/commands", new
        {
            commandName = "ping",
            requestHex = "02"
        });
        Assert.Equal(HttpStatusCode.OK, auto.StatusCode);
        var autoId = JsonSerializer.Deserialize<JsonElement>(await auto.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var autoCmd = await _client.GetFromJsonAsync<JsonElement>($"/api/craft/commands/{autoId}", JsonOpts);
        Assert.Equal((int)CraftCommandStatus.Succeeded, autoCmd!.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Destructive_command_requires_gate_and_live_simulation_flag_conflict()
    {
        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals", JsonOpts))![0].GetProperty("id").GetGuid();
        var s = await _client.PostAsJsonAsync("/api/craft/sessions",
            new { terminalId = term, technicianId = "tech-a", operatorId = "op-a", fieldNotes = "" });
        var sid = JsonSerializer.Deserialize<JsonElement>(await s.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var bad = await _client.PostAsJsonAsync($"/api/craft/sessions/{sid}/commands", new
        {
            commandName = "table_reload",
            requestHex = "AB",
            commandTypeCode = "craft.table_reload",
            confirmDestructive = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var badReason = await _client.PostAsJsonAsync($"/api/craft/sessions/{sid}/commands", new
        {
            commandName = "table_reload",
            requestHex = "AB",
            commandTypeCode = "craft.table_reload",
            confirmDestructive = true,
            auditReason = "no"
        });
        Assert.Equal(HttpStatusCode.BadRequest, badReason.StatusCode);

        var ok = await _client.PostAsJsonAsync($"/api/craft/sessions/{sid}/commands", new
        {
            commandName = "table_reload",
            requestHex = "AB",
            commandTypeCode = "craft.table_reload",
            confirmDestructive = true,
            auditReason = "approved maintenance window test"
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var live = await _client.PostAsJsonAsync($"/api/craft/sessions/{sid}/commands", new
        {
            commandName = "ping",
            requestHex = "00",
            simulationExecution = false
        });
        Assert.Equal(HttpStatusCode.Conflict, live.StatusCode);
    }

    [Fact]
    public async Task Terminal_diagnostics_snapshot_cdr_and_table_reload_requests()
    {
        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals", JsonOpts))![0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/terminals/{term}/diagnostics")).StatusCode);

        var snap = await _client.PostAsJsonAsync($"/api/terminals/{term}/diagnostics/snapshots",
            new { snapshotJson = """{"modem":"sim","lines":["power ok"]}""", source = "itest" });
        Assert.Equal(HttpStatusCode.Created, snap.StatusCode);

        var cdr = await _client.PostAsJsonAsync($"/api/terminals/{term}/request-cdr-upload",
            new { detailJson = """{"batch":"cdr-test"}""", simulationMode = true });
        Assert.Equal(HttpStatusCode.Created, cdr.StatusCode);

        var tr = await _client.PostAsJsonAsync($"/api/terminals/{term}/request-table-reload",
            new { detailJson = """{"tables":"all"}""", simulationMode = true });
        Assert.Equal(HttpStatusCode.Created, tr.StatusCode);

        var bundle = await _client.GetFromJsonAsync<JsonElement>($"/api/terminals/{term}/diagnostics", JsonOpts);
        Assert.True(bundle!.TryGetProperty("hardwareValidationNotice", out var hv) && hv.GetString()?.Length > 10);
        Assert.True(bundle.GetProperty("snapshots").GetArrayLength() >= 1);
        Assert.True(bundle.GetProperty("cdrUploadRequests").GetArrayLength() >= 1);
        Assert.True(bundle.GetProperty("tableReloadRequests").GetArrayLength() >= 1);

        var audits = await _client.GetFromJsonAsync<JsonElement>("/api/audit/events", JsonOpts);
        var sawDiag = false;
        foreach (var e in audits!.EnumerateArray())
        {
            if (e.GetProperty("category").GetString() == "terminals.diagnostics"
                && e.GetProperty("action").GetString() == "snapshot_saved")
            {
                sawDiag = true;
                break;
            }
        }
        Assert.True(sawDiag);
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }
}
