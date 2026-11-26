using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class SmartcardApplicationCatalogueTests(ApiFixture factory)
{
    private readonly HttpClient _client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);

    [Fact]
    public async Task Compatibility_matrix_exposes_distinct_application_subtypes_without_claiming_payments()
    {
        var result = await _client.GetFromJsonAsync<JsonElement>("/api/smartcards/applications");
        Assert.False(result.GetProperty("physicalCardTransactionsQualified").GetBoolean());
        Assert.True(result.GetProperty("referenceApplicationEvidenceOnly").GetBoolean());
        var rows = result.GetProperty("applications").EnumerateArray().ToArray();
        Assert.Equal(9, rows.Length);
        Assert.Equal(9, rows.Select(row => row.GetProperty("key").GetString()).Distinct().Count());
        Assert.Equal(2, rows.Count(row => row.GetProperty("transport").GetString() == "synchronous_memory"));
        Assert.Equal(7, rows.Count(row => row.GetProperty("transport").GetString() == "asynchronous_processor"));
        var enabled = Assert.Single(rows.Where(row => row.GetProperty("enabledInInspectedBuilds").GetBoolean()));
        Assert.Equal("gpm103-disposable", enabled.GetProperty("key").GetString());
        Assert.All(rows, row => Assert.Equal(SmartcardApplicationCatalogue.QualificationPending, row.GetProperty("qualification").GetString()));
        Assert.Equal(2, rows.Count(row => row.GetProperty("family").GetString() == "visa-cash"));
        Assert.Equal(3, rows.Count(row => row.GetProperty("family").GetString() == "smartcity"));
        Assert.Equal(3, rows.Count(row => row.GetProperty("implementationEvidence").GetString() == "historical_partial"));
    }
}
