using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;

namespace HostPlatform.Tests.Integration;

/// <summary>At least one HTTP call per API controller (plus minimal POST coverage where safe).</summary>
[Collection("IntegrationApi")]
public sealed class ControllersApiTests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Customers_crud_smoke()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/customers")).StatusCode);
        var create = await _client.PostAsJsonAsync("/api/customers", new { name = "ITest Co", code = $"IT{Guid.NewGuid():N}"[..8] });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = JsonSerializer.Deserialize<JsonElement>(await create.Content.ReadAsStringAsync(), JsonOpts);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/customers/{id}")).StatusCode);
        var put = await _client.PutAsJsonAsync($"/api/customers/{id}", new { name = "Renamed", code = "ZZ9" });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
    }

    [Fact]
    public async Task Sites_list_and_create()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/sites")).StatusCode);
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var r = await _client.PostAsJsonAsync("/api/sites", new { customerId = cust, name = "Site A", code = $"S{Guid.NewGuid():N}"[..6] });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    [Fact]
    public async Task Terminals_routes()
    {
        var sites = await _client.GetFromJsonAsync<JsonElement>("/api/sites");
        var siteId = sites![0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/terminals")).StatusCode);
        var create = await _client.PostAsJsonAsync("/api/terminals", new
        {
            siteId,
            terminalGroupId = (Guid?)null,
            transportEndpointId = (Guid?)null,
            firmwareVersionId = (Guid?)null,
            terminalIdHex = "AABBCCDDEE",
            displayName = "t-api",
            status = TerminalOperationalStatus.Provisioned
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = JsonSerializer.Deserialize<JsonElement>(await create.Content.ReadAsStringAsync(), JsonOpts);
        var tid = created.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/terminals/{tid}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/terminals/{tid}/events")).StatusCode);
        var st = await _client.PostAsJsonAsync($"/api/terminals/{tid}/status", new { status = TerminalOperationalStatus.Online, detail = "itest" });
        Assert.Equal(HttpStatusCode.OK, st.StatusCode);
        var sets = await _client.GetFromJsonAsync<JsonElement>("/api/tables/sets");
        var setId = sets![0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/terminals/{tid}/table-assignment")).StatusCode);
        var dl = await _client.PostAsJsonAsync($"/api/terminals/{tid}/downloads", new { tableSetId = setId });
        Assert.Equal(HttpStatusCode.Created, dl.StatusCode);
        var pkg = (await _client.GetFromJsonAsync<JsonElement>("/api/firmware/packages"))![0].GetProperty("id").GetGuid();
        var fj = await _client.PostAsJsonAsync($"/api/terminals/{tid}/firmware-jobs", new { firmwarePackageId = pkg, simulationMode = true });
        Assert.Equal(HttpStatusCode.Created, fj.StatusCode);
    }

    [Fact]
    public async Task Dlog_routes()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/dlog/message-types")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/dlog/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/dlog/transactions")).StatusCode);
        var txs = await _client.GetFromJsonAsync<JsonElement>("/api/dlog/transactions");
        var txId = txs![0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/dlog/transactions/{txId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/dlog/transactions/{txId}/payload")).StatusCode);
        var ingest = await _client.PostAsJsonAsync("/api/dlog/transactions/ingest", new
        {
            rawPayloadHex = "FE",
            terminalId = (Guid?)null,
            nccSessionId = (Guid?)null,
            sessionCorrelationId = "itest",
            messageType = (int?)null,
            firstByteIsMessageType = (bool?)true,
            direction = 1,
            clientIdempotencyKey = $"idem-{Guid.NewGuid():N}",
            capturedAtUtc = (DateTime?)null
        });
        Assert.Equal(HttpStatusCode.Created, ingest.StatusCode);
        var decode = await _client.PostAsJsonAsync("/api/dlog/decode", new { rawPayloadHex = "3F00", messageType = (int?)null, firstByteIsMessageType = true });
        Assert.Equal(HttpStatusCode.OK, decode.StatusCode);
        var replayDenied = await _client.PostAsJsonAsync("/api/dlog/replay", new { });
        Assert.Equal(HttpStatusCode.BadRequest, replayDenied.StatusCode);
        var rep = await _client.PostAsJsonAsync("/api/dlog/replay?confirm=true", new { confirmExport = true });
        Assert.Equal(HttpStatusCode.OK, rep.StatusCode);
        var audits = await _client.GetFromJsonAsync<JsonElement>("/api/audit/events");
        var hasDlogAudit = false;
        foreach (var ev in audits!.EnumerateArray())
        {
            if (string.Equals(ev.GetProperty("category").GetString(), "dlog", StringComparison.OrdinalIgnoreCase))
            {
                hasDlogAudit = true;
                break;
            }
        }

        Assert.True(hasDlogAudit);
    }

    [Fact]
    public async Task Dlog_rate_request_response_correlates()
    {
        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))![0].GetProperty("id").GetGuid();
        var session = "corr-itest";
        var req = await _client.PostAsJsonAsync("/api/dlog/transactions/ingest", new
        {
            rawPayloadHex = "3F01",
            terminalId = term,
            nccSessionId = (Guid?)null,
            sessionCorrelationId = session,
            messageType = (int?)null,
            firstByteIsMessageType = true,
            direction = 1,
            clientIdempotencyKey = $"r-{Guid.NewGuid():N}",
            capturedAtUtc = (DateTime?)null
        });
        Assert.Equal(HttpStatusCode.Created, req.StatusCode);
        var respPost = await _client.PostAsJsonAsync("/api/dlog/transactions/ingest", new
        {
            rawPayloadHex = "4002",
            terminalId = term,
            nccSessionId = (Guid?)null,
            sessionCorrelationId = session,
            messageType = (int?)null,
            firstByteIsMessageType = true,
            direction = 1,
            clientIdempotencyKey = $"s-{Guid.NewGuid():N}",
            capturedAtUtc = (DateTime?)null
        });
        Assert.Equal(HttpStatusCode.Created, respPost.StatusCode);
        var respId = JsonSerializer.Deserialize<JsonElement>(await respPost.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions/{respId}");
        var links = detail!.GetProperty("correlationLinks");
        Assert.Equal(JsonValueKind.Array, links.ValueKind);
        Assert.True(links.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Tables_routes()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/tables/definitions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/tables/versions")).StatusCode);
        var d = await _client.PostAsJsonAsync("/api/tables/definitions", new { name = "Tdef", tableNumber = 99, description = "itest" });
        Assert.Equal(HttpStatusCode.Created, d.StatusCode);
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var s = await _client.PostAsJsonAsync("/api/tables/sets", new { name = "Set IT", customerId = cust, isDefault = false });
        Assert.Equal(HttpStatusCode.Created, s.StatusCode);
        var sid = JsonSerializer.Deserialize<JsonElement>(await s.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/tables/sets/{sid}")).StatusCode);
        var defId = JsonSerializer.Deserialize<JsonElement>(await d.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/tables/definitions/{defId}")).StatusCode);
        var ver = await _client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = sid,
            tableDefinitionId = defId,
            tableRevision = 1,
            payloadBase64 = Convert.ToBase64String(new byte[] { 0x01 }),
            sortOrder = 0,
            dependsOnTableDefinitionId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, ver.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.PutAsJsonAsync($"/api/tables/sets/{sid}", new { name = "Set IT renamed", customerId = cust, isDefault = false }))
                .StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"/api/tables/sets/{sid}/publish?confirm=true", null)).StatusCode);
    }

    [Fact]
    public async Task Downloads_list_and_missing_detail()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/downloads")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/downloads/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Uploads_routes()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/uploads")).StatusCode);
        var post = await _client.PostAsJsonAsync("/api/uploads", new
        {
            terminalId = (Guid?)null,
            payloadHex = "DEAD",
            metadataJson = "{}",
            idempotencyKey = $"idem-{Guid.NewGuid():N}",
            relatedDlogTransactionId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var id = JsonSerializer.Deserialize<JsonElement>(await post.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/uploads/{id}")).StatusCode);
    }

    [Fact]
    public async Task Rating_routes()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/rate-plans")).StatusCode);
        var cust = (await _client.GetFromJsonAsync<JsonElement>("/api/customers"))![0].GetProperty("id").GetGuid();
        var p = await _client.PostAsJsonAsync("/api/rate-plans", new { name = "Plan IT", customerId = cust, mode = RatingMode.SetRated });
        Assert.Equal(HttpStatusCode.Created, p.StatusCode);
        var q = await _client.PostAsJsonAsync("/api/rating/quote", new { dialedDigits = "5551212", mode = RatingMode.RealTimeRated });
        Assert.Equal(HttpStatusCode.OK, q.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/call-records")).StatusCode);
    }

    [Fact]
    public async Task Cards_routes()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/cards/simulation-state")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/smartcards/types")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/cards/products")).StatusCode);
        var prod = await _client.PostAsJsonAsync("/api/cards/products", new { name = "Prod IT", code = $"P{Guid.NewGuid():N}"[..6] });
        Assert.Equal(HttpStatusCode.Created, prod.StatusCode);
        var pid = JsonSerializer.Deserialize<JsonElement>(await prod.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/cards/products/{pid}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/cards/accounts")).StatusCode);
        var acc = await _client.PostAsJsonAsync("/api/cards/accounts", new
        {
            cardProductId = pid,
            terminalId = (Guid?)null,
            panLast4 = "4242",
            balance = 10m
        });
        Assert.Equal(HttpStatusCode.Created, acc.StatusCode);
        var aid = JsonSerializer.Deserialize<JsonElement>(await acc.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/cards/accounts/{aid}")).StatusCode);
        var adj = await _client.PostAsJsonAsync($"/api/cards/accounts/{aid}/adjust-balance", new { delta = 1m, reason = "itest", simulationMode = true });
        Assert.Equal(HttpStatusCode.OK, adj.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/cards/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/cards/reconciliation-batches")).StatusCode);
    }

    [Fact]
    public async Task Craft_routes()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/craft/command-types")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/craft/sessions")).StatusCode);
        var term = (await _client.GetFromJsonAsync<JsonElement>("/api/terminals"))![0].GetProperty("id").GetGuid();
        var s = await _client.PostAsJsonAsync("/api/craft/sessions",
            new { terminalId = term, technicianId = "tech1", operatorId = "op1", fieldNotes = "api smoke" });
        Assert.Equal(HttpStatusCode.Created, s.StatusCode);
        var sid = JsonSerializer.Deserialize<JsonElement>(await s.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();
        var cmd = await _client.PostAsJsonAsync($"/api/craft/sessions/{sid}/commands", new { commandName = "ping", requestHex = "00" });
        Assert.Equal(HttpStatusCode.OK, cmd.StatusCode);
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/craft/sessions/{sid}", JsonOpts);
        Assert.True(detail!.TryGetProperty("commands", out var cmds) && cmds.GetArrayLength() >= 1);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/terminals/{term}/diagnostics")).StatusCode);
    }

    [Fact]
    public async Task Firmware_routes()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/firmware/packages")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/firmware/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/firmware/execution-policy")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/firmware/targets")).StatusCode);
        var create = await _client.PostAsJsonAsync("/api/firmware/packages", new
        {
            name = "Pkg IT",
            versionLabel = "0.0.1",
            checksumHex = "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20",
            artifactSizeBytes = 3L,
            metadataJson = "{}"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/firmware/jobs")).StatusCode);
        var jobs = await _client.GetFromJsonAsync<JsonElement>("/api/firmware/jobs");
        var jid = jobs![0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/firmware/jobs/{jid}")).StatusCode);
    }

    [Fact]
    public async Task Audit_and_ncc_routes()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/audit/events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/ncc/sessions")).StatusCode);
    }

    [Fact]
    public async Task Ncc_frame_capture_upload_and_inspect()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/ncc/frame-captures")).StatusCode);
        var bytes = Convert.FromHexString("022005780303");
        using var mp = new MultipartFormDataContent();
        mp.Add(new ByteArrayContent(bytes), "file", "capture.bin");
        var up = await _client.PostAsync("/api/ncc/frame-captures", mp);
        Assert.Equal(HttpStatusCode.Created, up.StatusCode);
        var created = JsonSerializer.Deserialize<JsonElement>(await up.Content.ReadAsStringAsync(), JsonOpts);
        var id = created.GetProperty("id").GetGuid();
        Assert.True(created.GetProperty("frameCount").GetInt32() >= 1);
        var get = await _client.GetAsync($"/api/ncc/frame-captures/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var detail = JsonSerializer.Deserialize<JsonElement>(await get.Content.ReadAsStringAsync(), JsonOpts);
        Assert.True(detail.GetProperty("streamItems").GetArrayLength() >= 1);

        var audits = await _client.GetFromJsonAsync<JsonElement>("/api/audit/events");
        Assert.Equal(JsonValueKind.Array, audits.ValueKind);
        var found = false;
        foreach (var ev in audits.EnumerateArray())
        {
            if (ev.GetProperty("category").GetString() == "ncc.frame_capture"
                && ev.GetProperty("action").GetString() == "upload")
            {
                found = true;
                break;
            }
        }
        Assert.True(found);
    }

    [Fact]
    public async Task Ncc_frame_capture_delete_requires_confirmation()
    {
        var bytes = Convert.FromHexString("022005780303");
        using var mp = new MultipartFormDataContent();
        mp.Add(new ByteArrayContent(bytes), "file", "del.bin");
        var up = await _client.PostAsync("/api/ncc/frame-captures", mp);
        Assert.Equal(HttpStatusCode.Created, up.StatusCode);
        var created = JsonSerializer.Deserialize<JsonElement>(await up.Content.ReadAsStringAsync(), JsonOpts);
        var id = created.GetProperty("id").GetGuid();
        var bad = await _client.DeleteAsync($"/api/ncc/frame-captures/{id}");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var ok = await _client.DeleteAsync($"/api/ncc/frame-captures/{id}?confirm=true");
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/ncc/frame-captures/{id}")).StatusCode);
        var audits = await _client.GetFromJsonAsync<JsonElement>("/api/audit/events");
        var sawDelete = false;
        foreach (var ev in audits.EnumerateArray())
        {
            if (ev.GetProperty("category").GetString() == "ncc.frame_capture"
                && ev.GetProperty("action").GetString() == "delete")
            {
                sawDelete = true;
                break;
            }
        }
        Assert.True(sawDelete);
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }
}
