using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class CardTranche8Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Payment_applies_to_balance_in_simulation_mode()
    {
        var prod = await _client.PostAsJsonAsync("/api/cards/products", new
        {
            name = "Ledger T8",
            code = $"L8{Guid.NewGuid():N}"[..8],
            defaultCardType = CardType.Magstripe,
            allowNegativeBalance = false,
            isTestFixtureCatalogEntry = true
        });
        var pid = JsonSerializer.Deserialize<JsonElement>(await prod.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var acc = await _client.PostAsJsonAsync("/api/cards/accounts", new
        {
            cardProductId = pid,
            terminalId = (Guid?)null,
            panLast4 = "4242",
            balance = 10m,
            resolvedCardType = CardType.Unknown,
            credentialTokenRef = "tok",
            credentialKind = CardCredentialKind.OpaqueToken
        });
        var aid = JsonSerializer.Deserialize<JsonElement>(await acc.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        await _client.PostAsJsonAsync($"/api/cards/accounts/{aid}/adjust-balance", new
        {
            delta = 2m,
            reason = "top-up",
            simulationMode = true
        });

        await _client.PostAsJsonAsync("/api/cards/transactions", new
        {
            cardAccountId = aid,
            amount = -1.25m,
            reconciliation = ReconciliationStatus.Pending,
            detailJson = "{}",
            reportedCardType = CardType.Unknown,
            rawPayloadJson = "{}"
        });

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/cards/accounts/{aid}", JsonOpts);
        Assert.Equal(10.75m, detail!.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Payment_rejected_when_balance_would_go_negative()
    {
        var prod = await _client.PostAsJsonAsync("/api/cards/products", new
        {
            name = "Ledger neg",
            code = $"LN{Guid.NewGuid():N}"[..8],
            defaultCardType = CardType.Magstripe,
            allowNegativeBalance = false,
            isTestFixtureCatalogEntry = true
        });
        var pid = JsonSerializer.Deserialize<JsonElement>(await prod.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var acc = await _client.PostAsJsonAsync("/api/cards/accounts", new
        {
            cardProductId = pid,
            terminalId = (Guid?)null,
            panLast4 = "0000",
            balance = 3m,
            resolvedCardType = CardType.Unknown,
            credentialTokenRef = "tok",
            credentialKind = CardCredentialKind.OpaqueToken
        });
        var aid = JsonSerializer.Deserialize<JsonElement>(await acc.Content.ReadAsStringAsync(), JsonOpts).GetProperty("id").GetGuid();

        var denied = await _client.PostAsJsonAsync("/api/cards/transactions", new
        {
            cardAccountId = aid,
            amount = -10m,
            reconciliation = ReconciliationStatus.Pending,
            detailJson = "{}",
            rawPayloadJson = "{}"
        });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }
}
