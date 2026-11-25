using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class FieldEventCatalogueTests(ApiFixture factory)
{
    [Fact]
    public async Task Catalogue_preserves_decoded_meaning_and_original_report_while_editing_and_clearing_settings()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "fixture");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        const string path = "/api/field-events/reference/alarm/22";
        var report = new DlogTransaction
        {
            MessageType = 7, Direction = 1, RawPayload = [7, 26, 11, 23, 12, 0, 0, 22],
            CapturedAtUtc = DateTime.UtcNow, IdempotencyKey = Guid.NewGuid().ToString("N")
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.DlogTransactions.Add(report);
            await db.SaveChangesAsync();
        }
        using var invalid = await client.PutAsJsonAsync(path, new { operatorDescription = "Fixture", severity = 5, revision = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var created = await client.PutAsJsonAsync(path, new { operatorDescription = "Inspect backup supply", severity = 2, revision = 0 });
        created.EnsureSuccessStatusCode();
        var result = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Memory lost", result.GetProperty("description").GetString());
        Assert.Equal(1, result.GetProperty("revision").GetInt32());
        using var stale = await client.PutAsJsonAsync(path, new { operatorDescription = "Stale", severity = 3, revision = 0 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var same = await client.PutAsJsonAsync(path, new { operatorDescription = "Inspect backup supply", severity = 2, revision = 1 });
        Assert.Equal(1, (await same.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt32());
        var row = (await client.GetFromJsonAsync<JsonElement>("/api/field-events")).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == report.Id);
        Assert.Equal("Memory lost (alarm 22)", row.GetProperty("summary").GetString());
        Assert.Equal("Inspect backup supply", row.GetProperty("operatorDescription").GetString());
        Assert.Equal(2, row.GetProperty("severity").GetInt32());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions/{report.Id}");
        Assert.Equal("Inspect backup supply", detail.GetProperty("fieldEvent").GetProperty("operatorDescription").GetString());
        Assert.Equal(Convert.ToHexString(report.RawPayload), detail.GetProperty("rawPayloadHex").GetString());

        using var unknown = await client.PutAsJsonAsync("/api/field-events/reference/maintenance/50000",
            new { operatorDescription = "Fixture maintenance report", severity = (int?)null, revision = 0 });
        unknown.EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("description").ValueKind);
        var catalogue = await client.GetFromJsonAsync<JsonElement>("/api/field-events/reference");
        Assert.Contains(catalogue.GetProperty("maintenance").EnumerateArray(), item => item.GetProperty("code").GetInt32() == 50000);
        using var noSeverity = await client.PutAsJsonAsync("/api/field-events/reference/maintenance/50000",
            new { operatorDescription = "Fixture", severity = 1, revision = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, noSeverity.StatusCode);

        using var cleared = await client.PutAsJsonAsync(path, new { operatorDescription = "", severity = (int?)null, revision = 1 });
        cleared.EnsureSuccessStatusCode();
        var clearResult = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, clearResult.GetProperty("revision").GetInt32());
        Assert.Equal(JsonValueKind.Null, clearResult.GetProperty("severity").ValueKind);
        await using var finalScope = factory.Services.CreateAsyncScope();
        var savedDb = finalScope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        Assert.Equal(report.RawPayload, (await savedDb.DlogTransactions.FindAsync(report.Id))!.RawPayload);
        Assert.Equal(2, await savedDb.AuditEvents.CountAsync(e => e.Category == "field_event_catalogue" && e.Resource == "alarm:22"));
    }
}
