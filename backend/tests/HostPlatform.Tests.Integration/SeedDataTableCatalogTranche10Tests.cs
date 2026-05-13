using System.Net.Http.Json;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

/// <summary>
/// Tranche 10: seeded <see cref="HostPlatform.Domain.TableDefinition"/> rows must carry operator-grade catalog text (GAP-0012), not placeholder copy.
/// </summary>
[Collection("IntegrationApi")]
public sealed class SeedDataTableCatalogTranche10Tests(ApiFixture factory)
{
    private readonly HttpClient _client = CreateClient(factory);

    [Fact]
    public async Task Table_definitions_have_catalog_descriptions_for_seed_numbers()
    {
        var defs = await _client.GetFromJsonAsync<JsonElement>("/api/tables/definitions");

        var byNumber = new Dictionary<int, JsonElement>();
        foreach (var d in defs.EnumerateArray())
            byNumber[d.GetProperty("tableNumber").GetInt32()] = d;

        Assert.True(byNumber.TryGetValue(10, out var t10));
        Assert.True(byNumber.TryGetValue(20, out var t20));
        Assert.True(byNumber.TryGetValue(30, out var t30));

        foreach (var label in new[] { t10, t20, t30 })
        {
            var desc = label.GetProperty("description").GetString() ?? "";
            Assert.DoesNotContain("MVP placeholder", desc, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("opaque blob", desc, StringComparison.OrdinalIgnoreCase);
            Assert.True(desc.Length >= 80, "Catalog description should be substantive for operator UI.");
        }

        Assert.Contains("Firmware rate-table", t10.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("NPA", t20.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("instant-win", t30.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Rate table (firmware payload)", t10.GetProperty("name").GetString());
        Assert.Equal("LCD / NPA–NXX dialing tables", t20.GetProperty("name").GetString());
        Assert.Equal("Instant-win configuration", t30.GetProperty("name").GetString());
    }

    private static HttpClient CreateClient(ApiFixture f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "itest@local");
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        return c;
    }
}
