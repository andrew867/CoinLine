using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class ConfigurationLibraryTests(ApiFixture factory)
{
    [Fact]
    public async Task Library_pages_named_records_with_firmware_assignment_and_duplicate_context_without_payloads()
    {
        var definition = new TableDefinition { Name = "Library advertising", TableNumber = 29 };
        var duplicate = new TableDefinition { Name = "Library alternate", TableNumber = 29 };
        var firmware = new FirmwareVersion { Label = "Library fixture firmware" };
        var sets = Enumerable.Range(0, 51).Select(index => new TableSet
        {
            Name = $"Library fixture {index:D2}",
            Status = index == 0 ? TableSetStatus.Published : TableSetStatus.Draft,
            TargetFirmwareVersionId = index == 0 ? firmware.Id : null
        }).ToArray();
        var empty = new TableSet { Name = "Library empty fixture" };
        var records = sets.Select((set, index) => new TableVersion
        {
            TableSetId = set.Id, TableDefinitionId = definition.Id, TableRevision = 1,
            ConfigurationId = index < 2 ? "00A001" : null, EmbeddedPayload = [29, 0, 1],
            PayloadSha256Hex = TablePayloadHasher.Sha256Hex([29, 0, 1])
        }).ToArray();
        var normalized = new TablePayload { RawContent = [29, 1, 2, 3], LengthBytes = 4, Sha256Hex = TablePayloadHasher.Sha256Hex([29, 1, 2, 3]) };
        var otherRecord = new TableVersion
        {
            TableSetId = sets[0].Id, TableDefinitionId = duplicate.Id, TableRevision = 2,
            ConfigurationId = "00A002", TablePayloadId = normalized.Id, PayloadSha256Hex = normalized.Sha256Hex
        };
        var customer = new Customer { Name = "Library fixture", Code = "LIBRARY" };
        var site = new Site { CustomerId = customer.Id, Name = "Library site", Code = "LIBRARY" };
        var terminal = new Terminal { SiteId = site.Id, DisplayName = "Library phone", TerminalIdHex = "AA01020306" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(definition, duplicate, firmware, empty, normalized, otherRecord, customer, site, terminal,
                new TerminalTableAssignment { TerminalId = terminal.Id, TableSetId = sets[0].Id });
            db.AddRange(sets);
            db.AddRange(records);
            await db.SaveChangesAsync();
        }
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        const string path = "/api/tables/configuration-library";
        var options = await client.GetFromJsonAsync<JsonElement>($"{path}/options");
        Assert.Contains(options.GetProperty("sets").EnumerateArray(), row => row.GetProperty("id").GetGuid() == sets[0].Id);
        Assert.DoesNotContain(options.GetProperty("sets").EnumerateArray(), row => row.GetProperty("id").GetGuid() == empty.Id);
        Assert.Contains(options.GetProperty("firmware").EnumerateArray(), row => row.GetProperty("id").GetGuid() == firmware.Id);

        var page1 = await client.GetFromJsonAsync<JsonElement>($"{path}?query=LIBRARY%20FIXTURE&number=29&sort=set");
        var page2 = await client.GetFromJsonAsync<JsonElement>($"{path}?query=library%20fixture&number=29&sort=set&page=2");
        Assert.Equal(52, page1.GetProperty("total").GetInt32());
        Assert.Equal(50, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(2, page2.GetProperty("items").GetArrayLength());
        Assert.Equal(52, page1.GetProperty("items").EnumerateArray().Concat(page2.GetProperty("items").EnumerateArray())
            .Select(row => row.GetProperty("id").GetGuid()).Distinct().Count());
        var targeted = await client.GetFromJsonAsync<JsonElement>($"{path}?firmwareVersionId={firmware.Id}&status=published&configurationId=00a002");
        var record = Assert.Single(targeted.GetProperty("items").EnumerateArray());
        Assert.Equal(otherRecord.Id, record.GetProperty("id").GetGuid());
        Assert.Equal("Published", ((TableSetStatus)record.GetProperty("setStatus").GetInt32()).ToString());
        Assert.Equal(2, record.GetProperty("recordCountInSet").GetInt32());
        Assert.Equal(1, record.GetProperty("assignedPhoneCount").GetInt32());
        Assert.Equal(4, record.GetProperty("payloadLengthBytes").GetInt32());
        Assert.False(record.GetProperty("nativeLayoutKnown").GetBoolean());
        Assert.Equal(normalized.Sha256Hex, record.GetProperty("payloadSha256Hex").GetString());
        Assert.False(record.TryGetProperty("rawContent", out _));
        Assert.False(record.TryGetProperty("embeddedPayload", out _));
        Assert.False(record.TryGetProperty("validationDiagnosticsJson", out _));
        var untargeted = await client.GetFromJsonAsync<JsonElement>($"{path}?untargeted=true&status=draft&configurationId=00a001");
        var inline = Assert.Single(untargeted.GetProperty("items").EnumerateArray());
        Assert.Equal(records[1].Id, inline.GetProperty("id").GetGuid());
        Assert.Equal(3, inline.GetProperty("payloadLengthBytes").GetInt32());
        using var invalid = await client.GetAsync($"{path}?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
