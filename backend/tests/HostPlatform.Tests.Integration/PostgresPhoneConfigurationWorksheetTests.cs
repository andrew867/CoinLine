using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresPhoneConfigurationWorksheetTests
{
    [PostgresIntegrationFact]
    public async Task Independent_configuration_lookups_and_atomic_assignment_preserve_existing_records_and_separate_download()
    {
        await using var ncc = new NccFactory(); var key = Guid.NewGuid().ToString("N");
        await using var host = Factory(ncc, key); using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var password = Guid.NewGuid().ToString("N"); var reader = Account(password, new() { ["table-assignments"] = 1 });
        var editor = Account(password, new() { ["table-assignments"] = 3, ["downloads"] = 5 });
        var owner = Customer(); var foreign = Customer(); var site = Site(owner);
        var firmware = new FirmwareVersion { Label = "Fixture 200-slot target", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var later = new FirmwareVersion { Label = "Fixture 180-slot target", TableProfileId = FirmwareTableProfiles.LaterMultiPay180 };
        var phone = Phone(site, firmware); var otherPhone = Phone(site, firmware);
        var definitions = new[] { 21, 26, 29, 31, 32, 50 }.ToDictionary(number => number,
            number => new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" });
        var features = Bytes(26, 72, 2); var oldAds = Bytes(29, 481, 1); var newAds = Bytes(29, 481, 2);
        var oldCoins = Bytes(50, 105, 25); var newCoins = Bytes(50, 105, 30); oldCoins[65] = newCoins[65] = 3;
        var servicing = Bytes(31, 37, 4); var stats = Bytes(32, 33);
        var basis = Set(owner, firmware, "Basis", Version(26, features, "FEAT01"), Version(29, oldAds, "ADV001"),
            Version(31, servicing, "SERV01"), Version(32, stats, "STAT01"), Version(50, oldCoins, "COIN01"));
        var ads = Set(owner, firmware, "Advertising source", Version(26, features, "FEAT01"), Version(29, newAds, "ADV002", 26));
        var coins = Set(null, firmware, "Shared coin source", Version(26, features, "FEAT01"), Version(50, newCoins, "COIN02", 26));
        var conflicting = Set(owner, firmware, "Conflicting coin source", Version(26, Bytes(26, 72, 3), "FEAT99"), Version(50, newCoins, "COIN03", 26));
        var otherOwner = Set(foreign, firmware, "Foreign source", Version(29, newAds, "OTHER1"));
        var draft = Set(owner, firmware, "Draft source", Version(29, newAds, "DRAFT1")); draft.Status = TableSetStatus.Draft;
        var incompatible = Set(owner, later, "Wrong target", Version(29, newAds, "WRONG1"));
        var badLength = Set(owner, firmware, "Wrong length", Version(29, Bytes(29, 480), "WRONG2"));
        var pagingLabel = Guid.NewGuid().ToString("N")[..8];
        var paging = Enumerable.Range(0, 52).Select(index => Set(owner, firmware, $"{pagingLabel} {index:D2}",
            Version(29, newAds, $"A{index:D5}"))).ToArray();
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = basis };
        var otherAssignment = new TerminalTableAssignment { Terminal = otherPhone, TableSet = basis };
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.True(db.Database.IsNpgsql());
            db.AddRange(reader, editor, owner, foreign, firmware, later, basis, ads, coins, conflicting, otherOwner, draft, incompatible, badLength, assignment, otherAssignment);
            db.AddRange(paging); await db.SaveChangesAsync();
        }
        var endpoint = $"/api/terminals/{phone.Id}/configuration-worksheet";
        using (var anonymous = await client.GetAsync(endpoint)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await SignIn(client, reader, password);
        var worksheet = await client.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.Equal(JsonValueKind.Null, worksheet.GetProperty("hostRating").ValueKind);
        Assert.Equal(basis.Id, worksheet.GetProperty("baseSetId").GetGuid()); Assert.False(worksheet.GetProperty("canUpdate").GetBoolean());
        Assert.Equal("ADV001", Row(worksheet, 29).GetProperty("assigned").GetProperty("configurationId").GetString());
        Assert.True(Row(worksheet, 32).GetProperty("canClear").GetBoolean()); Assert.False(Row(worksheet, 31).GetProperty("canClear").GetBoolean());
        Assert.DoesNotContain(worksheet.GetProperty("records").EnumerateArray(), row => row.GetProperty("number").GetInt32() == 94);
        var firstPage = await client.GetFromJsonAsync<JsonElement>($"{endpoint}/options?number=29&query={pagingLabel.ToUpperInvariant()}&page=1");
        Assert.Equal(52, firstPage.GetProperty("total").GetInt32()); Assert.Equal(50, firstPage.GetProperty("records").GetArrayLength());
        var secondPage = await client.GetFromJsonAsync<JsonElement>($"{endpoint}/options?number=29&query={pagingLabel}&page=2");
        Assert.Equal(new[] { "A00050", "A00051" }, secondPage.GetProperty("records").EnumerateArray().Select(row => row.GetProperty("configurationId").GetString()));
        Assert.Equal(0, (await Lookup("OTHER1")).GetProperty("total").GetInt32()); Assert.Equal(0, (await Lookup("DRAFT1")).GetProperty("total").GetInt32());
        Assert.Contains("Requires", Assert.Single((await Lookup("WRONG1")).GetProperty("records").EnumerateArray()).GetProperty("issue").GetString());
        Assert.Contains("481", Assert.Single((await Lookup("WRONG2")).GetProperty("records").EnumerateArray()).GetProperty("issue").GetString());
        var selection = new[] { Copy(29, ads), Copy(50, coins), Clear(32) };
        var readOnly = await Preview(worksheet, selection); Assert.False(readOnly.GetProperty("canApply").GetBoolean());
        using (var denied = await client.PostAsJsonAsync(endpoint, Input(worksheet, selection, Token(readOnly)))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await SignIn(client, editor, password);
        using (var denied = await client.GetAsync("/api/tables/sets")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.GetAsync($"/api/tables/sets/{ads.Id}/records/29/fields")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        worksheet = await client.GetFromJsonAsync<JsonElement>(endpoint); Assert.True(worksheet.GetProperty("canUpdate").GetBoolean());
        using (var rejected = await client.PostAsJsonAsync(endpoint + "/preview", Input(worksheet, new[] { Copy(29, otherOwner) }))) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using (var rejected = await client.PostAsJsonAsync(endpoint + "/preview", Input(worksheet, new[] { Copy(29, ads), Copy(50, conflicting) })))
        { Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Assert.Contains("different MT26", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()); }
        using (var rejected = await client.PostAsJsonAsync(endpoint + "/preview", Input(worksheet, new[] { Clear(31) }))) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var preview = await Preview(worksheet, selection);
        Assert.True(preview.GetProperty("canApply").GetBoolean());
        var changes = preview.GetProperty("changes").EnumerateArray().ToDictionary(row => row.GetProperty("tableNumber").GetInt32());
        Assert.Equal("keep", changes[26].GetProperty("action").GetString()); Assert.True(changes[26].GetProperty("required").GetBoolean());
        Assert.Equal("ADV002", changes[29].GetProperty("result").GetProperty("configurationId").GetString());
        Assert.Equal("COIN02", changes[50].GetProperty("result").GetProperty("configurationId").GetString());
        Assert.Equal("remove", changes[32].GetProperty("action").GetString()); Assert.Equal(JsonValueKind.Null, changes[32].GetProperty("result").ValueKind);
        var responses = await Task.WhenAll(client.PostAsJsonAsync(endpoint, Input(worksheet, selection, Token(preview))), client.PostAsJsonAsync(endpoint, Input(worksheet, selection, Token(preview))));
        using var accepted = Assert.Single(responses, row => row.StatusCode == HttpStatusCode.OK);
        using var competing = Assert.Single(responses, row => row.StatusCode == HttpStatusCode.Conflict);
        var result = await accepted.Content.ReadFromJsonAsync<JsonElement>(); var newSet = result.GetProperty("tableSetId").GetGuid();
        Assert.Equal(basis.Id, result.GetProperty("previousTableSetId").GetGuid());
        Assert.Equal(new[] { 29, 50 }, result.GetProperty("changedNumbers").EnumerateArray().Select(row => row.GetInt32()).Order());
        Assert.Equal(32, Assert.Single(result.GetProperty("removedNumbers").EnumerateArray()).GetInt32());
        IReadOnlyList<TableVersion> expected;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var updated = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TablePayload).Include(row => row.Versions).ThenInclude(row => row.TableDefinition).SingleAsync(row => row.Id == newSet);
            Assert.Equal(TableSetStatus.Published, updated.Status); Assert.Equal(owner.Id, updated.CustomerId);
            var records = updated.Versions.ToDictionary(row => row.TableDefinition!.TableNumber); Assert.Equal(new[] { 26, 29, 31, 50 }, records.Keys.Order());
            Assert.Equal(features, TableDistributionService.GetEffectivePayload(records[26])); Assert.Equal(servicing, TableDistributionService.GetEffectivePayload(records[31]));
            Assert.Equal(newAds, TableDistributionService.GetEffectivePayload(records[29])); Assert.Equal(newCoins, TableDistributionService.GetEffectivePayload(records[50]));
            Assert.Equal(2, records[29].TableRevision); Assert.Equal(1, records[31].TableRevision);
            Assert.Equal(basis.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.Id == otherAssignment.Id)).TableSetId);
            Assert.Equal(oldAds, (await db.TableVersions.SingleAsync(row => row.TableSetId == basis.Id && row.TableDefinitionId == definitions[29].Id)).EmbeddedPayload);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id)); Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == phone.Id));
            expected = TableDistributionService.SelectDataDownloadRecords(updated.Versions.ToArray(), [29, 50]);
        }
        var refreshed = await client.GetFromJsonAsync<JsonElement>(endpoint); Assert.Equal(newSet, refreshed.GetProperty("baseSetId").GetGuid());
        Assert.Equal("ADV002", Row(refreshed, 29).GetProperty("assigned").GetProperty("configurationId").GetString());
        Assert.Equal(JsonValueKind.Null, Row(refreshed, 32).GetProperty("assigned").ValueKind);
        var unchanged = await Preview(refreshed, selection); Assert.False(unchanged.GetProperty("canApply").GetBoolean());
        using var queued = await client.PostAsJsonAsync($"/api/terminals/{phone.Id}/downloads", new { tableSetId = newSet, scope = "Partial",
            partialTableDefinitionIds = new[] { definitions[29].Id, definitions[50].Id } }); queued.EnsureSuccessStatusCode();
        var batch = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var busy = await client.PostAsJsonAsync(endpoint + "/preview", Input(refreshed, selection))) Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        Assert.Equal(new[] { 26, 29, 50 }, expected.Select(row => row.TableDefinition!.TableNumber).Order());
        await Transfer(ncc.Port, phone.TerminalIdHex, expected.Select(TableDistributionService.GetEffectivePayload).ToArray());
        for (var i = 0; i < 50; i++)
        {
            var download = await client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batch}");
            if (download.GetProperty("status").GetInt32() == 2)
            { Assert.Equal(3, download.GetProperty("timeline").GetArrayLength()); Assert.All(download.GetProperty("timeline").EnumerateArray(), row => Assert.Equal("ack", row.GetProperty("lastAckStatus").GetString())); break; }
            if (i == 49) Assert.Fail("The selected-record batch did not complete.");
            await Task.Delay(100);
        }

        Task<JsonElement> Lookup(string term) => client.GetFromJsonAsync<JsonElement>($"{endpoint}/options?number=29&query={term}");
        async Task<JsonElement> Preview(JsonElement current, Choice[] choices)
        { using var response = await client.PostAsJsonAsync(endpoint + "/preview", Input(current, choices)); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
        TableVersion Version(int number, byte[] bytes, string configuration, int? dependency = null) => new() { TableDefinition = definitions[number],
            TableDefinitionId = definitions[number].Id, EmbeddedPayload = bytes, TableRevision = 1, ConfigurationId = configuration,
            DependsOnTableDefinitionId = dependency != null ? definitions[dependency.Value].Id : null, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(bytes) };
    }

    [PostgresIntegrationFact]
    public async Task Initial_assignment_requires_explicit_MT21_personalization_and_reports_missing_families()
    {
        await using var ncc = new NccFactory(); var key = Guid.NewGuid().ToString("N");
        await using var host = Factory(ncc, key); using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var password = Guid.NewGuid().ToString("N"); var editor = Account(password, new() { ["table-assignments"] = 3 });
        var owner = Customer(); var phone = Phone(Site(owner), new FirmwareVersion { Label = "Fixture target", TableProfileId = FirmwareTableProfiles.MultiPay200 });
        var sourceBytes = Bytes(21, 18); Convert.FromHexString("0000000001").CopyTo(sourceBytes, 1); sourceBytes[8] = 0x42;
        var definition = new TableDefinition { TableNumber = 21, Name = "Fixture access" };
        var source = Set(owner, phone.FirmwareVersion!, "Shared access template", new TableVersion { TableDefinition = definition, TableDefinitionId = definition.Id,
            TableRevision = 1, EmbeddedPayload = sourceBytes, ConfigurationId = "ACC001", PayloadSha256Hex = TablePayloadHasher.Sha256Hex(sourceBytes) });
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.AddRange(editor, phone, source); await db.SaveChangesAsync();
        }
        await SignIn(client, editor, password);
        var endpoint = $"/api/terminals/{phone.Id}/configuration-worksheet"; var worksheet = await client.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.Equal(JsonValueKind.Null, worksheet.GetProperty("baseSetId").ValueKind);
        var choices = new[] { Copy(21, source) };
        using (var wrong = await client.PostAsJsonAsync(endpoint + "/preview", Input(worksheet, choices)))
        { Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode); Assert.Contains("wire ID", (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()); }
        using var previewResponse = await client.PostAsJsonAsync(endpoint + "/preview", Input(worksheet, choices, personalize: true)); previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("canApply").GetBoolean()); Assert.True(preview.GetProperty("initialAssignment").GetBoolean());
        Assert.True(Assert.Single(preview.GetProperty("changes").EnumerateArray()).GetProperty("personalized").GetBoolean());
        Assert.Contains(preview.GetProperty("family").GetProperty("tables").EnumerateArray(), row => row.GetProperty("required").GetBoolean() && row.GetProperty("status").GetString() == "missing");
        using var applied = await client.PostAsJsonAsync(endpoint, Input(worksheet, choices, Token(preview), personalize: true)); applied.EnsureSuccessStatusCode();
        var result = await applied.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(JsonValueKind.Null, result.GetProperty("previousTableSetId").ValueKind);
        var newSet = result.GetProperty("tableSetId").GetGuid();
        await using var readScope = host.Services.CreateAsyncScope(); var database = readScope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var stored = await database.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == newSet);
        var expected = sourceBytes.ToArray(); Convert.FromHexString(phone.TerminalIdHex).CopyTo(expected, 1);
        Assert.Equal(expected, TableDistributionService.GetEffectivePayload(stored));
        Assert.Equal(sourceBytes, (await database.TableVersions.SingleAsync(row => row.TableSetId == source.Id)).EmbeddedPayload);
        Assert.False(await database.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id));
    }

    [PostgresIntegrationFact]
    public async Task Published_starter_stages_all_compatible_records_then_initial_assignment_and_full_download_are_separate()
    {
        await using var ncc = new NccFactory(); var key = Guid.NewGuid().ToString("N");
        await using var host = Factory(ncc, key); using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var password = Guid.NewGuid().ToString("N");
        var reader = Account(password, new() { ["table-assignments"] = 1 });
        var denied = Account(password, new() { ["phones"] = 1 });
        var editor = Account(password, new() { ["table-assignments"] = 3, ["downloads"] = 5 });
        var owner = Customer(); var foreign = Customer(); var site = Site(owner);
        var firmware = new FirmwareVersion { Label = "Fixture native target", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var different = new FirmwareVersion { Label = "Fixture different target", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var observed = new FirmwareVersion { Label = "Fixture observed target", TableProfileId = FirmwareTableProfiles.Mtr19Desk };
        var phone = Phone(site, firmware); var observedPhone = Phone(site, observed);
        var definitions = new[] { 21, 26, 29, 50, 94 }.ToDictionary(number => number,
            number => new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" });
        var access = Bytes(21, 18); Convert.FromHexString("0000000001").CopyTo(access, 1); access[8] = 0x42;
        var features = Bytes(26, 72, 2); var ads = Bytes(29, 481, 1); var coins = Bytes(50, 105, 25); coins[65] = 3;
        var starter = Set(owner, firmware, "Preferred starter", Version(21, access), Version(26, features), Version(29, ads, 26), Version(50, coins));
        starter.IsDefault = true;
        var shared = Set(null, firmware, "Shared starter", Version(29, ads));
        var bad = Set(owner, firmware, "Wrong-length starter", Version(29, Bytes(29, 480)));
        var privateSource = Set(foreign, firmware, "Other customer", Version(29, ads));
        var wrongFirmware = Set(owner, different, "Other firmware", Version(29, ads));
        var code = Set(owner, firmware, "Code schedule", Version(94, Bytes(94, 100)));
        var draft = Set(owner, firmware, "Unpublished starter", Version(29, ads)); draft.Status = TableSetStatus.Draft;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(reader, denied, editor, phone, observedPhone, starter, shared, bad, privateSource, wrongFirmware, code, draft);
            await db.SaveChangesAsync();
        }
        var endpoint = $"/api/terminals/{phone.Id}/configuration-worksheet"; var sources = endpoint + "/initial-sources";
        using (var anonymous = await client.GetAsync(sources)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await SignIn(client, denied, password);
        using (var response = await client.GetAsync(sources)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await SignIn(client, reader, password);
        var listed = await client.GetFromJsonAsync<JsonElement>(sources); Assert.True(listed.GetProperty("available").GetBoolean());
        Assert.Equal(3, listed.GetProperty("total").GetInt32());
        var rows = listed.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(starter.Id, rows[0].GetProperty("id").GetGuid()); Assert.True(rows[0].GetProperty("hasAccess").GetBoolean());
        Assert.Equal(4, rows[0].GetProperty("recordCount").GetInt32());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>(sources + "?query=SHARED")).GetProperty("total").GetInt32());
        foreach (var excluded in new[] { privateSource, wrongFirmware, code, draft })
            using (var response = await client.GetAsync($"{sources}/{excluded.Id}")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await client.GetAsync($"{sources}/{bad.Id}")) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unavailable = await client.GetFromJsonAsync<JsonElement>($"/api/terminals/{observedPhone.Id}/configuration-worksheet/initial-sources");
        Assert.False(unavailable.GetProperty("available").GetBoolean());
        var detail = await client.GetFromJsonAsync<JsonElement>($"{sources}/{starter.Id}");
        Assert.True(detail.GetProperty("requiresAccessPersonalization").GetBoolean());
        var staged = detail.GetProperty("records").EnumerateArray().Select(row => new Choice(row.GetProperty("number").GetInt32(), "copy",
            row.GetProperty("tableSetId").GetGuid(), row.GetProperty("id").GetGuid())).ToArray();
        Assert.Equal(new[] { 21, 26, 29, 50 }, staged.Select(row => row.Number));
        Assert.All(staged, row => Assert.Equal(starter.Id, row.SourceSetId));
        var worksheet = await client.GetFromJsonAsync<JsonElement>(endpoint);
        using (var response = await client.PostAsJsonAsync(endpoint, Input(worksheet, staged, personalize: true))) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await SignIn(client, editor, password);
        using (var response = await client.PostAsJsonAsync(endpoint + "/preview", Input(worksheet, staged))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var previewResponse = await client.PostAsJsonAsync(endpoint + "/preview", Input(worksheet, staged, personalize: true));
        previewResponse.EnsureSuccessStatusCode(); var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("initialAssignment").GetBoolean()); Assert.True(preview.GetProperty("canApply").GetBoolean());
        Assert.Equal(4, preview.GetProperty("changes").GetArrayLength());
        Assert.Contains(preview.GetProperty("family").GetProperty("tables").EnumerateArray(), row => row.GetProperty("required").GetBoolean() && row.GetProperty("status").GetString() == "missing");
        using var applied = await client.PostAsJsonAsync(endpoint, Input(worksheet, staged, Token(preview), personalize: true)); applied.EnsureSuccessStatusCode();
        var result = await applied.Content.ReadFromJsonAsync<JsonElement>(); var newSet = result.GetProperty("tableSetId").GetGuid();
        using (var response = await client.GetAsync(sources)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await client.PostAsJsonAsync(endpoint, Input(worksheet, staged, Token(preview), personalize: true))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var expectedAccess = access.ToArray(); Convert.FromHexString(phone.TerminalIdHex).CopyTo(expectedAccess, 1);
        var expected = new Dictionary<int, byte[]> { [21] = expectedAccess, [26] = features, [29] = ads, [50] = coins };
        IReadOnlyList<TableVersion> order;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == phone.Id));
            var records = await db.TableVersions.Include(row => row.TableDefinition).Include(row => row.TablePayload).Where(row => row.TableSetId == newSet).ToArrayAsync();
            Assert.Equal(4, records.Length);
            foreach (var record in records) Assert.Equal(expected[record.TableDefinition!.TableNumber], TableDistributionService.GetEffectivePayload(record));
            var originalRecords = await db.TableVersions.Include(row => row.TableDefinition).Include(row => row.TablePayload)
                .Where(row => row.TableSetId == starter.Id).ToArrayAsync();
            Assert.Equal(staged.Select(row => row.SourceVersionId!.Value).Order(), originalRecords.Select(row => row.Id).Order());
            foreach (var record in originalRecords)
            {
                var number = record.TableDefinition!.TableNumber;
                Assert.Equal(number == 21 ? access : expected[number], TableDistributionService.GetEffectivePayload(record));
            }
            order = TableDistributionService.OrderVersionsForDownload(records);
        }
        using var queued = await client.PostAsJsonAsync($"/api/terminals/{phone.Id}/downloads", new { tableSetId = newSet, scope = "Full" }); queued.EnsureSuccessStatusCode();
        var batchId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Transfer(ncc.Port, phone.TerminalIdHex, order.Select(TableDistributionService.GetEffectivePayload).ToArray());
        for (var i = 0; i < 50; i++)
        {
            var download = await client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batchId}");
            if (download.GetProperty("status").GetInt32() == 2)
            { Assert.Equal(4, download.GetProperty("timeline").GetArrayLength()); Assert.All(download.GetProperty("timeline").EnumerateArray(), row => Assert.Equal("ack", row.GetProperty("lastAckStatus").GetString())); break; }
            if (i == 49) Assert.Fail("The separately queued full batch did not complete.");
            await Task.Delay(100);
        }

        TableVersion Version(int number, byte[] bytes, int? dependency = null) => new() { TableDefinition = definitions[number], TableDefinitionId = definitions[number].Id,
            EmbeddedPayload = bytes, TableRevision = 1, ConfigurationId = $"FIX{number:D3}",
            DependsOnTableDefinitionId = dependency != null ? definitions[dependency.Value].Id : null, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(bytes) };
    }

    private sealed record Choice(int Number, string Action, Guid? SourceSetId = null, Guid? SourceVersionId = null);
    private static Choice Copy(int number, TableSet source) => new(number, "copy", source.Id, source.Versions.Single(row => row.TableDefinition!.TableNumber == number).Id);
    private static Choice Clear(int number) => new(number, "clear");
    private static object Input(JsonElement current, Choice[] choices, string? token = null, bool personalize = false) => new
    { baseSetId = current.GetProperty("baseSetId"), assignmentRevision = current.GetProperty("assignmentRevision"), choices, personalizeAccessIdentity = personalize, previewToken = token };
    private static string Token(JsonElement preview) => preview.GetProperty("previewToken").GetString()!;
    private static JsonElement Row(JsonElement worksheet, int number) => worksheet.GetProperty("records").EnumerateArray().Single(row => row.GetProperty("number").GetInt32() == number);
    private static byte[] Bytes(int number, int length, byte value = 0) { var bytes = new byte[length]; bytes[0] = (byte)number; bytes[1] = value; return bytes; }
    private static TableSet Set(Customer? owner, FirmwareVersion firmware, string label, params TableVersion[] versions) => new() { Name = label + " " + Guid.NewGuid().ToString("N")[..6],
        Customer = owner, CustomerId = owner?.Id, TargetFirmwareVersion = firmware, TargetFirmwareVersionId = firmware.Id, Status = TableSetStatus.Published,
        Versions = versions.Select((row, index) => { row.SortOrder = index; return row; }).ToList() };
    private static Customer Customer() => new() { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
    private static Site Site(Customer customer) => new() { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
    private static Terminal Phone(Site site, FirmwareVersion firmware) => new() { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Random.Shared.NextInt64(10, 9999999999).ToString("D10"),
        FirmwareVersion = firmware, FirmwareVersionId = firmware.Id, TerminalFamilyId = "mtr19-multipay" };
    private static OperatorAccount Account(string password, Dictionary<string, int> permissions) => new() { Username = "fixture-" + Guid.NewGuid().ToString("N"),
        DisplayName = "Fixture operator", Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(permissions) };
    private static WebApplicationFactory<Program> Factory(NccFactory ncc, string key) => ncc.WithWebHostBuilder(web => web.UseEnvironment("Production")
        .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
        .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
        .UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
    private static async Task SignIn(HttpClient client, OperatorAccount account, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = account.Username, password }); login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
    }
    private static async Task Transfer(int port, string wireId, IReadOnlyList<byte[]> expected)
    {
        var wire = Convert.FromHexString(wireId); using var tcp = new TcpClient(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await tcp.ConnectAsync(IPAddress.Loopback, port, deadline.Token); var stream = tcp.GetStream();
        await stream.WriteAsync(Packet(0, [44, 1]), deadline.Token); AssertAck(await Read());
        var header = await Read(); Assert.Equal(new byte[] { 18 }, header.Data); await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)), deadline.Token);
        byte id = 1;
        foreach (var record in expected)
        {
            var received = new List<byte>();
            while (received.Count < record.Length) { var next = await Read(); received.AddRange(next.Data); await stream.WriteAsync(Packet((byte)(NccControl.Ack | next.Control)), deadline.Token); }
            Assert.Equal(record, received); await stream.WriteAsync(Packet(id, [14, record[0]]), deadline.Token); AssertAck(await Read()); id = NccControl.PacketId((byte)(id + 1));
        }
        var end = await Read(); Assert.Equal(new byte[] { 13 }, end.Data); await stream.WriteAsync(Packet((byte)(NccControl.Ack | NccControl.Clr | end.Control)), deadline.Token);
        byte[] Packet(byte control, byte[]? data = null) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] });
        static void AssertAck(NccWirePacket packet) { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }
        async Task<NccWirePacket> Read()
        {
            var head = new byte[3]; await stream.ReadExactlyAsync(head, deadline.Token); var raw = new byte[NccFrameLayout.GetExpectedWireLength(head[2])]; head.CopyTo(raw, 0);
            await stream.ReadExactlyAsync(raw.AsMemory(3), deadline.Token); var decoded = NccFrameCodec.TryDecode(raw, NccParseMode.Strict); Assert.True(decoded.Success); return decoded.Packet!;
        }
    }
}
