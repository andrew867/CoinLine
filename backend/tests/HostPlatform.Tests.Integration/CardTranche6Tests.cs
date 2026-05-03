using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class CardTranche6Tests(ApiFixture factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Simulation_state_and_smartcard_types_and_products_detail()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/cards/simulation-state")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/smartcards/types")).StatusCode);

        var products = await _client.GetFromJsonAsync<JsonElement>("/api/cards/products", JsonOpts);
        var firstId = products[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/cards/products/{firstId}")).StatusCode);
    }

    [Fact]
    public async Task Create_product_account_adjust_audit_negative_balance_read_unknown_transaction_reconcile()
    {
        var prod = await _client.PostAsJsonAsync("/api/cards/products", new
        {
            name = "Tranche6 IT",
            code = $"T6{Guid.NewGuid():N}"[..8],
            defaultCardType = CardType.Magstripe,
            allowNegativeBalance = false,
            isTestFixtureCatalogEntry = true
        });
        Assert.Equal(HttpStatusCode.Created, prod.StatusCode);
        var pid = JsonSerializer.Deserialize<JsonElement>(await prod.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var acc = await _client.PostAsJsonAsync("/api/cards/accounts", new
        {
            cardProductId = pid,
            terminalId = (Guid?)null,
            panLast4 = "4242",
            balance = 10m,
            resolvedCardType = CardType.Unknown,
            credentialTokenRef = "vault-ref-test-001",
            credentialKind = CardCredentialKind.OpaqueToken
        });
        Assert.Equal(HttpStatusCode.Created, acc.StatusCode);
        var aid = JsonSerializer.Deserialize<JsonElement>(await acc.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/cards/accounts/{aid}", JsonOpts);
        Assert.True(detail.TryGetProperty("credentialTokenMasked", out var masked));
        Assert.Contains("…", masked.GetString());

        var adj = await _client.PostAsJsonAsync($"/api/cards/accounts/{aid}/adjust-balance", new
        {
            delta = -20m,
            reason = "expect reject negative balance",
            simulationMode = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, adj.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync($"/api/cards/accounts/{aid}/adjust-balance", new
            {
                delta = 2m,
                reason = "lab top-up with audit trail",
                simulationMode = true
            })).StatusCode);

        var badAudit = await _client.PostAsJsonAsync($"/api/cards/accounts/{aid}/adjust-balance", new
        {
            delta = 1m,
            reason = "no",
            simulationMode = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, badAudit.StatusCode);

        var read = await _client.PostAsJsonAsync("/api/cards/read-events", new
        {
            cardAccountId = aid,
            reportedCardType = CardType.Unknown,
            rawPayloadJson = """{"opaque":"unknown-chip-vendor","bytesHex":"deadbeef"}"""
        });
        Assert.Equal(HttpStatusCode.Created, read.StatusCode);

        var tx = await _client.PostAsJsonAsync("/api/cards/transactions", new
        {
            cardAccountId = aid,
            amount = -1.25m,
            reconciliation = ReconciliationStatus.Pending,
            detailJson = """{"kind":"debit"}""",
            reportedCardType = CardType.Unknown,
            rawPayloadJson = """{"terminalReported":"UNKNOWN_RAIL","payload":[1,2,3]}"""
        });
        Assert.Equal(HttpStatusCode.Created, tx.StatusCode);

        var listTx = await _client.GetAsync($"/api/cards/transactions?cardAccountId={aid}");
        Assert.Equal(HttpStatusCode.OK, listTx.StatusCode);

        var batch = await _client.PostAsJsonAsync("/api/cards/reconciliation-batches", new { detailJson = """{"note":"t6"}""" });
        Assert.Equal(HttpStatusCode.Created, batch.StatusCode);
        var bid = JsonSerializer.Deserialize<JsonElement>(await batch.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/cards/reconciliation-batches/{bid}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/cards/reconciliation-batches")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/api/cards/reconciliation-batches/{bid}/post", new { })).StatusCode);

        var closeFail = await _client.PostAsJsonAsync($"/api/cards/reconciliation-batches/{bid}/close", new { confirm = false });
        Assert.Equal(HttpStatusCode.BadRequest, closeFail.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync($"/api/cards/reconciliation-batches/{bid}/close", new { confirm = true })).StatusCode);

        var audits = await _client.GetFromJsonAsync<JsonElement>("/api/audit/events", JsonOpts);
        Assert.True(audits.GetArrayLength() > 0);
        var sawCards = false;
        foreach (var e in audits.EnumerateArray())
        {
            if (e.GetProperty("category").GetString() == "cards")
                sawCards = true;
        }

        Assert.True(sawCards);
    }

    [Fact]
    public async Task Write_event_simulated_records_created_and_audit()
    {
        var w = await _client.PostAsJsonAsync("/api/cards/write-events", new
        {
            intendedOperation = "simulated_load",
            rawPayloadJson = """{"harness":true}""",
            simulationMode = true
        });
        Assert.Equal(HttpStatusCode.Created, w.StatusCode);
    }

    [Fact]
    public async Task Reconciliation_exception_requires_confirm_and_audit()
    {
        var batch = await _client.PostAsJsonAsync("/api/cards/reconciliation-batches", new { detailJson = """{"t":"exc"}""" });
        Assert.Equal(HttpStatusCode.Created, batch.StatusCode);
        var bid = JsonSerializer.Deserialize<JsonElement>(await batch.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var deny = await _client.PostAsJsonAsync($"/api/cards/reconciliation-batches/{bid}/exception", new { confirm = false });
        Assert.Equal(HttpStatusCode.BadRequest, deny.StatusCode);

        var ok = await _client.PostAsJsonAsync($"/api/cards/reconciliation-batches/{bid}/exception", new
        {
            confirm = true,
            note = "forced exception for integration test"
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/cards/reconciliation-batches/{bid}", JsonOpts);
        Assert.Equal(CardReconciliationBatchStatus.Exception, (CardReconciliationBatchStatus)detail.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Physical_write_blocked_without_simulation()
    {
        var w = await _client.PostAsJsonAsync("/api/cards/write-events", new
        {
            intendedOperation = "load_value",
            rawPayloadJson = "{}",
            simulationMode = false
        });
        Assert.Equal(HttpStatusCode.Conflict, w.StatusCode);
    }
}
