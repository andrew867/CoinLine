using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class FirmwareTranche8Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>64-char lowercase SHA256 hex placeholder for integration tests.</summary>
    private const string TestShaA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }

    [Fact]
    public async Task Execution_policy_defaults_live_flashing_off()
    {
        var pol = await _client.GetFromJsonAsync<JsonElement>("/api/firmware/execution-policy", JsonOpts);
        Assert.False(pol.GetProperty("allowLiveFlashing").GetBoolean());
        Assert.Contains("HARDWARE_VALIDATION", pol.GetProperty("hardwareValidationNotice").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Primary_artifact_checksum_must_match_package()
    {
        var create = await _client.PostAsJsonAsync("/api/firmware/packages", new
        {
            name = "chk pkg",
            versionLabel = "1.0",
            checksumHex = TestShaA,
            artifactSizeBytes = 3L,
            metadataJson = "{}"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var pkgId = JsonSerializer.Deserialize<JsonElement>(await create.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var bad = await _client.PostAsJsonAsync($"/api/firmware/packages/{pkgId}/artifacts", new
        {
            kind = "primary",
            sha256Hex = new string('0', 64),
            sizeBytes = 3L,
            storageRef = "blob:test",
            metadataJson = "{}"
        });
        Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode);
        var ok = await _client.PostAsJsonAsync($"/api/firmware/packages/{pkgId}/artifacts", new
        {
            kind = "primary",
            sha256Hex = TestShaA,
            sizeBytes = 3L,
            storageRef = "blob:test",
            metadataJson = "{}"
        });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Incompatible_terminal_rejected_when_rules_present()
    {
        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))![0];
        var termId = term.GetProperty("id").GetGuid();
        var ver = await _client.PostAsJsonAsync("/api/firmware/versions", new
        {
            label = "Non-matching FW",
            buildId = "test",
            notes = "itest"
        });
        Assert.Equal(HttpStatusCode.Created, ver.StatusCode);
        var otherFwId = JsonSerializer.Deserialize<JsonElement>(await ver.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var pkgCreate = await _client.PostAsJsonAsync("/api/firmware/packages", new
        {
            name = "rule pkg",
            versionLabel = "1.0",
            checksumHex = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            artifactSizeBytes = 1L,
            metadataJson = "{}"
        });
        var pkgId = JsonSerializer.Deserialize<JsonElement>(await pkgCreate.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var rule = await _client.PostAsJsonAsync($"/api/firmware/packages/{pkgId}/compatibility-rules", new
        {
            requiredTerminalFirmwareVersionId = otherFwId,
            requiredTargetSkuContains = (string?)null,
            notes = "terminal must match this fw id"
        });
        Assert.Equal(HttpStatusCode.Created, rule.StatusCode);
        var job = await _client.PostAsJsonAsync($"/api/terminals/{termId}/firmware-jobs", new
        {
            firmwarePackageId = pkgId,
            simulationMode = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, job.StatusCode);
    }

    [Fact]
    public async Task Simulation_job_flow_simulate_approve_cancel()
    {
        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))![0];
        var termId = term.GetProperty("id").GetGuid();
        var pkgCreate = await _client.PostAsJsonAsync("/api/firmware/packages", new
        {
            name = "flow pkg",
            versionLabel = "1.0",
            checksumHex = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            artifactSizeBytes = 1L,
            metadataJson = "{}"
        });
        var pkgId = JsonSerializer.Deserialize<JsonElement>(await pkgCreate.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var jobCreate = await _client.PostAsJsonAsync($"/api/terminals/{termId}/firmware-jobs", new
        {
            firmwarePackageId = pkgId,
            simulationMode = true
        });
        Assert.Equal(HttpStatusCode.Created, jobCreate.StatusCode);
        var jobId = JsonSerializer.Deserialize<JsonElement>(await jobCreate.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var sim = await _client.PostAsync($"/api/firmware/jobs/{jobId}/simulate", null);
        Assert.Equal(HttpStatusCode.OK, sim.StatusCode);

        var jobDetail = await _client.GetFromJsonAsync<JsonElement>($"/api/firmware/jobs/{jobId}", JsonOpts);
        var steps = jobDetail!.GetProperty("steps");
        JsonElement? dlaStep = null;
        foreach (var st in steps.EnumerateArray())
        {
            if (st.GetProperty("name").GetString() == "dla_xmodem_transport")
            {
                dlaStep = st;
                break;
            }
        }

        Assert.True(dlaStep.HasValue);
        Assert.True(dlaStep!.Value.GetProperty("succeeded").GetBoolean());
        Assert.Equal((int)FirmwareUpdateStepStatus.Succeeded, dlaStep.Value.GetProperty("stepStatus").GetInt32());

        var badApprove = await _client.PostAsJsonAsync($"/api/firmware/jobs/{jobId}/approve", new { rollbackNotes = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, badApprove.StatusCode);

        var okApprove = await _client.PostAsJsonAsync($"/api/firmware/jobs/{jobId}/approve",
            new { rollbackNotes = "Backup verified on spare terminal before field rollout." });
        Assert.Equal(HttpStatusCode.OK, okApprove.StatusCode);

        var pkg2 = await _client.PostAsJsonAsync("/api/firmware/packages", new
        {
            name = "cancel pkg",
            versionLabel = "1.0",
            checksumHex = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            artifactSizeBytes = 1L,
            metadataJson = "{}"
        });
        var pkg2Id = JsonSerializer.Deserialize<JsonElement>(await pkg2.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var job2 = await _client.PostAsJsonAsync($"/api/terminals/{termId}/firmware-jobs", new
        {
            firmwarePackageId = pkg2Id,
            simulationMode = true
        });
        var job2Id = JsonSerializer.Deserialize<JsonElement>(await job2.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var cancelDenied = await _client.PostAsJsonAsync($"/api/firmware/jobs/{job2Id}/cancel", new { confirm = false, reason = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, cancelDenied.StatusCode);

        var cancel = await _client.PostAsJsonAsync($"/api/firmware/jobs/{job2Id}/cancel",
            new { confirm = true, reason = "operator abort" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/firmware/jobs/{job2Id}", JsonOpts);
        Assert.Equal((int)FirmwareUpdateJobStatus.Cancelled, detail!.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Simulate_writes_simulate_start_audit_action()
    {
        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))![0];
        var termId = term.GetProperty("id").GetGuid();
        var pkgCreate = await _client.PostAsJsonAsync("/api/firmware/packages", new
        {
            name = "audit pkg",
            versionLabel = "1.0",
            checksumHex = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            artifactSizeBytes = 1L,
            metadataJson = "{}"
        });
        var pkgId = JsonSerializer.Deserialize<JsonElement>(await pkgCreate.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var jobCreate = await _client.PostAsJsonAsync($"/api/terminals/{termId}/firmware-jobs", new
        {
            firmwarePackageId = pkgId,
            simulationMode = true
        });
        var jobId = JsonSerializer.Deserialize<JsonElement>(await jobCreate.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/firmware/jobs/{jobId}/simulate", null)).StatusCode);

        var audits = await _client.GetFromJsonAsync<JsonElement>("/api/audit/events", JsonOpts);
        var found = false;
        foreach (var ev in audits!.EnumerateArray())
        {
            if (ev.GetProperty("category").GetString() == "firmware.job"
                && ev.GetProperty("action").GetString() == "simulate_start")
            {
                found = true;
                break;
            }
        }
        Assert.True(found);
    }

    [Fact]
    public async Task Live_job_without_config_is_blocked()
    {
        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))![0];
        var termId = term.GetProperty("id").GetGuid();
        var pkgs = await _client.GetFromJsonAsync<JsonElement>("/api/firmware/packages");
        var pkgId = pkgs![0].GetProperty("id").GetGuid();
        var job = await _client.PostAsJsonAsync($"/api/terminals/{termId}/firmware-jobs", new
        {
            firmwarePackageId = pkgId,
            simulationMode = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, job.StatusCode);
    }
}
