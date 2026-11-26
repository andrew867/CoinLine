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
public sealed class PostgresScreeningBulkTests
{
    private const int Slot = 7;
    private static readonly NativeTableLayout Layout = NativeTableLayouts.DraftTemplates(92).Single(row => row.Sections.Count == 200);
    private const int Offset = 1 + (Slot - 1) * 17;
    private static readonly Dictionary<string, int> FullPermissions = new() { ["table-records"] = 3, ["table-sets"] = 7,
        ["table-assignments"] = 1, ["downloads"] = 5, ["phones"] = 1 };

    [PostgresIntegrationFact]
    public async Task Bulk_add_is_atomic_and_prepares_immutable_replacement_then_exact_selected_NCC_download()
    {
        await using var ncc = new NccFactory();
        var key = Guid.NewGuid().ToString("N");
        await using var host = Factory(ncc, key);
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var password = Guid.NewGuid().ToString("N");
        var editor = Account(password, FullPermissions);
        var reader = Account(password, new() { ["table-records"] = 1 });
        var recordEditor = Account(password, new() { ["table-records"] = 3 });
        var owner = Customer(); var otherOwner = Customer();
        var definition = new TableDefinition { Name = "Fixture screening", TableNumber = 92 };
        var otherDefinition = new TableDefinition { Name = "Fixture coin", TableNumber = 50 };
        var source = Set(owner, definition, Entry("3335550123", 0xA0), "Source", descriptions: new() { [Slot] = "Fixture route" });
        var draft = Set(owner, definition, Blank(0xC0), "Draft", descriptions: new() { [2] = "Unrelated note" });
        var original = Set(owner, definition, Blank(0xD0), "Published", published: true);
        var coin = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(50).Single());
        original.Versions.Add(Version(otherDefinition, coin, order: 2));
        var conflict = Set(owner, definition, Entry("3335550124", 0x60), "Occupied");
        var incompatible = Set(owner, definition, NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(92).Single(row => row.Sections.Count == 180)), "Incompatible");
        var otherCustomer = Set(otherOwner, definition, Entry("3335550125", 0x40), "Other customer");
        var duplicate = Set(owner, definition, Blank(0x10), "Duplicate");
        duplicate.Versions.Add(Version(definition, Blank(0x20), order: 2));
        var site = Site(owner); var phone = Phone(site);
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = original };
        var originalRaw = original.Versions.Single(row => row.TableDefinition == definition).EmbeddedPayload!.ToArray();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.True(db.Database.IsNpgsql());
            db.AddRange(editor, reader, recordEditor, source, draft, original, conflict, incompatible, otherCustomer, duplicate, assignment);
            await db.SaveChangesAsync();
        }

        var endpoint = Endpoint(source);
        using (var anonymous = await client.PostAsJsonAsync(endpoint + "/preview", Request(source, "add"))) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await SignIn(client, reader, password);
        var readOnly = await Preview(client, source, "add");
        Assert.False(readOnly.GetProperty("canApply").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Row(readOnly, original).GetProperty("assignedPhones").ValueKind);
        using (var forbidden = await client.PostAsJsonAsync(endpoint, Request(source, "add", token: Token(readOnly)))) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await SignIn(client, editor, password);
        var blocked = await Preview(client, source, "add");
        Assert.False(blocked.GetProperty("canApply").GetBoolean());
        Assert.Equal("blocked", Row(blocked, conflict).GetProperty("status").GetString());
        Assert.Equal("excluded", Row(blocked, incompatible).GetProperty("status").GetString());
        Assert.Equal("excluded", Row(blocked, duplicate).GetProperty("status").GetString());
        Assert.DoesNotContain(blocked.GetProperty("targets").EnumerateArray(), row => row.GetProperty("setId").GetGuid() == otherCustomer.Id);
        var global = await Preview(client, source, "add", scope: "all");
        Assert.Equal("blocked", Row(global, otherCustomer).GetProperty("status").GetString());
        using (var refused = await client.PostAsJsonAsync(endpoint, Request(source, "add", token: Token(blocked)))) Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        await AssertBytes(host.Services, draft.Id, Blank(0xC0));

        // Blanking retains the policy and is deliberately not the same as a vacant slot.
        await EditSlot(client, conflict, "blank");
        blocked = await Preview(client, source, "add");
        Assert.Equal("blocked", Row(blocked, conflict).GetProperty("status").GetString());
        await EditSlot(client, conflict, "delete");
        await SignIn(client, recordEditor, password);
        var limited = await Preview(client, source, "add");
        Assert.Contains("Table Sets Add", Row(limited, original).GetProperty("reason").GetString());
        var draftsOnly = await Preview(client, source, "add", includePublished: false);
        Assert.True(draftsOnly.GetProperty("canApply").GetBoolean());
        Assert.Equal("excluded", Row(draftsOnly, original).GetProperty("status").GetString());
        await SignIn(client, editor, password);
        var stale = await Preview(client, source, "add");
        Assert.True(stale.GetProperty("canApply").GetBoolean());
        Assert.Equal(1, stale.GetProperty("replacementDrafts").GetInt32());
        Assert.Equal(2, stale.GetProperty("updatedDrafts").GetInt32());
        await EditOtherSlot(client, draft);
        using (var refused = await client.PostAsJsonAsync(endpoint, Request(source, "add", token: Token(stale)))) Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var ready = await Preview(client, source, "add");
        var beforeDraft = await ReadBytes(host.Services, draft.Id);
        var expectedDraft = ExpectedCopy(source.Versions.Single().EmbeddedPayload!, beforeDraft);
        var expectedReplacement = ExpectedCopy(source.Versions.Single().EmbeddedPayload!, originalRaw);
        var responses = await Task.WhenAll(client.PostAsJsonAsync(endpoint, Request(source, "add", token: Token(ready))),
            client.PostAsJsonAsync(endpoint, Request(source, "add", token: Token(ready))));
        using var applied = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        using var competing = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var result = await applied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, result.GetProperty("affectedCount").GetInt32());
        var replacement = Assert.Single(result.GetProperty("configurations").EnumerateArray(), row => row.GetProperty("replacement").GetBoolean());
        Assert.Equal(original.Id, replacement.GetProperty("sourceSetId").GetGuid());
        var replacementId = replacement.GetProperty("setId").GetGuid();
        await AssertBytes(host.Services, source.Id, source.Versions.Single().EmbeddedPayload!);
        await AssertBytes(host.Services, draft.Id, expectedDraft);
        await AssertBytes(host.Services, original.Id, originalRaw);
        await AssertBytes(host.Services, replacementId, expectedReplacement);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var newSet = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition).Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == replacementId);
            Assert.Equal(TableSetStatus.Draft, newSet.Status); Assert.Equal(owner.Id, newSet.CustomerId);
            Assert.Equal(coin, TableDistributionService.GetEffectivePayload(newSet.Versions.Single(row => row.TableDefinition!.TableNumber == 50)));
            var changed = newSet.Versions.Single(row => row.TableDefinition!.TableNumber == 92);
            Assert.Equal("Fixture route", JsonSerializer.Deserialize<Dictionary<int, string>>(changed.ScreeningDescriptionsJson!)![Slot]);
            var target = await db.TableVersions.SingleAsync(row => row.TableSetId == draft.Id && row.TableDefinitionId == definition.Id);
            var descriptions = JsonSerializer.Deserialize<Dictionary<int, string>>(target.ScreeningDescriptionsJson!)!;
            Assert.Equal("Unrelated note", descriptions[2]); Assert.Equal("Fixture route", descriptions[Slot]);
            Assert.Equal(original.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id));
        }

        // Publication and the selected-record download remain deliberate, separate actions.
        using (var published = await client.PostAsync($"/api/tables/sets/{replacementId}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();
        using var queued = await client.PostAsJsonAsync($"/api/terminals/{phone.Id}/downloads", new { tableSetId = replacementId,
            scope = "Partial", partialTableDefinitionIds = new[] { definition.Id } });
        queued.EnsureSuccessStatusCode();
        var batchId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Transfer(ncc.Port, phone.TerminalIdHex, expectedReplacement);
        for (var i = 0; i < 50; i++)
        {
            var batch = await client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batchId}");
            if (batch.GetProperty("status").GetInt32() == 2)
            {
                Assert.Equal("ack", Assert.Single(batch.GetProperty("timeline").EnumerateArray()).GetProperty("lastAckStatus").GetString());
                break;
            }
            if (i == 49) Assert.Fail("The selected screening download did not complete.");
            await Task.Delay(100);
        }
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(original.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
        }
    }

    [PostgresIntegrationFact]
    public async Task Bulk_delete_checks_assignments_and_known_dependencies_then_keeps_other_entries_and_empty_drafts()
    {
        await using var ncc = new NccFactory();
        var key = Guid.NewGuid().ToString("N");
        await using var host = Factory(ncc, key);
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var password = Guid.NewGuid().ToString("N"); var editor = Account(password, FullPermissions);
        var owner = Customer(); var definition = new TableDefinition { Name = "Fixture screening", TableNumber = 92 };
        var features = new TableDefinition { Name = "Fixture features", TableNumber = 26 };
        var source = Set(owner, definition, Entry("3335550123", 0x90), "Source", descriptions: new() { [Slot] = "Clear this", [3] = "Preserve" });
        var referenced = Set(owner, definition, Entry("3335550124", 0x80), "Referenced");
        var assigned = Set(owner, definition, Entry("3335550125", 0x70), "Assigned", published: true);
        var unused = Set(owner, definition, Entry("3335550126", 0x60), "Unused published", published: true);
        var overridden = Set(owner, definition, Entry("3335550127", 0x50), "Override");
        var featureBytes = new byte[72]; featureBytes[0] = 26;
        var featureLayout = NativeTableLayouts.Find(26, featureBytes, NativeTableLayouts.MultiPay)!;
        featureBytes = featureLayout.Apply(featureBytes, new Dictionary<string, string> { ["operatorReference"] = Slot.ToString() });
        referenced.Versions.Add(Version(features, featureBytes, 2));
        var site = Site(owner); var phone = Phone(site); var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = assigned };
        var referenceOverride = new SiteTableOverride { Site = site, TableDefinition = definition, TableVersion = overridden.Versions.Single() };
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(editor, source, referenced, unused, overridden, assignment, referenceOverride); await db.SaveChangesAsync();
        }
        await SignIn(client, editor, password);
        var blocked = await Preview(client, source, "delete");
        Assert.Equal(3, blocked.GetProperty("blockedCount").GetInt32());
        Assert.Contains("assigned", Row(blocked, assigned).GetProperty("reason").GetString());
        Assert.Contains("MT26", Row(blocked, referenced).GetProperty("reason").GetString());
        Assert.Contains("overridden", Row(blocked, overridden).GetProperty("reason").GetString());
        using (var refused = await client.PostAsJsonAsync(Endpoint(source), Request(source, "delete", token: Token(blocked)))) Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        await AssertBytes(host.Services, source.Id, source.Versions.Single().EmbeddedPayload!);
        var fieldsPath = $"/api/tables/sets/{referenced.Id}/records/26/fields";
        var fields = await client.GetFromJsonAsync<JsonElement>(fieldsPath);
        using (var cleared = await client.PutAsJsonAsync(fieldsPath, new { revision = fields.GetProperty("revision").GetInt32(), sha256 = fields.GetProperty("sha256").GetString(),
            profile = fields.GetProperty("profile").GetString(), changes = new Dictionary<string, string> { ["operatorReference"] = "0" } })) cleared.EnsureSuccessStatusCode();
        // Only the isolated fixture removes its current assignment and override; the application cannot delete an in-use slot.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.Remove(await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id));
            db.Remove(await db.SiteTableOverrides.SingleAsync(row => row.Id == referenceOverride.Id)); await db.SaveChangesAsync();
        }
        var ready = await Preview(client, source, "delete");
        Assert.True(ready.GetProperty("canApply").GetBoolean());
        Assert.Equal(3, ready.GetProperty("updatedDrafts").GetInt32()); Assert.Equal(2, ready.GetProperty("replacementDrafts").GetInt32());
        using var applied = await client.PostAsJsonAsync(Endpoint(source), Request(source, "delete", token: Token(ready)));
        applied.EnsureSuccessStatusCode();
        var result = await applied.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(5, result.GetProperty("affectedCount").GetInt32());
        foreach (var row in result.GetProperty("configurations").EnumerateArray())
        {
            var original = new[] { source, referenced, assigned, unused, overridden }.Single(set => set.Id == row.GetProperty("sourceSetId").GetGuid());
            var before = original.Versions.Single(version => version.TableDefinition == definition).EmbeddedPayload!;
            var changed = await ReadBytes(host.Services, row.GetProperty("setId").GetGuid());
            Assert.True(CallScreeningWire.IsVacant(changed, Layout, Slot));
            Assert.Equal(before[..Offset], changed[..Offset]); Assert.Equal(before[(Offset + 12)..(Offset + 13)], changed[(Offset + 12)..(Offset + 13)]);
            Assert.Equal(before[(Offset + 14)..], changed[(Offset + 14)..]);
            if (row.GetProperty("replacement").GetBoolean()) await AssertBytes(host.Services, original.Id, before);
        }
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var current = await db.TableVersions.SingleAsync(row => row.TableSetId == source.Id && row.TableDefinitionId == definition.Id);
            var descriptions = JsonSerializer.Deserialize<Dictionary<int, string>>(current.ScreeningDescriptionsJson!)!;
            Assert.Equal("Preserve", descriptions[3]); Assert.False(descriptions.ContainsKey(Slot));
            Assert.True(await db.TableSets.AnyAsync(row => row.Id == source.Id));
        }
    }

    [PostgresIntegrationFact]
    public async Task Identical_MT59_lengths_do_not_establish_layout_compatibility()
    {
        await using var ncc = new NccFactory();
        var key = Guid.NewGuid().ToString("N"); await using var host = Factory(ncc, key);
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var password = Guid.NewGuid().ToString("N"); var editor = Account(password, FullPermissions);
        var owner = Customer(); var definition = new TableDefinition { Name = "Fixture compact screening", TableNumber = 59 };
        var compact = NativeTableLayouts.DraftTemplates(59).Single(row => row.Profile == CallScreeningWire.CompactProfile);
        var expanded = NativeTableLayouts.DraftTemplates(59).Single(row => row.Profile == CallScreeningWire.ExpandedProfile);
        var sourceBytes = compact.Apply(NativeTableLayouts.CreateDraftTemplate(compact), new Dictionary<string, string> { ["rule-6.pattern"] = "90?" });
        var compactBytes = NativeTableLayouts.CreateDraftTemplate(compact); const int offset = 1 + (Slot - 1) * 12;
        compactBytes[offset + 1] = 0xA0; compactBytes[offset + 3] = 0x80;
        var source = Set(owner, definition, sourceBytes, "Compact source");
        var target = Set(owner, definition, compactBytes, "Compact target");
        var wrong = Set(owner, definition, NativeTableLayouts.CreateDraftTemplate(expanded), "Expanded target");
        var ambiguous = Set(owner, definition, NativeTableLayouts.CreateDraftTemplate(compact), "Unconfirmed target");
        source.Versions.Single().ScreeningLayoutProfile = target.Versions.Single().ScreeningLayoutProfile = compact.Profile;
        wrong.Versions.Single().ScreeningLayoutProfile = expanded.Profile;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.AddRange(editor, source, target, wrong, ambiguous); await db.SaveChangesAsync();
        }
        await SignIn(client, editor, password);
        var endpoint = $"/api/tables/sets/{source.Id}/records/59/screening-bulk";
        var version = source.Versions.Single();
        object Input(string? token = null) => new { revision = version.TableRevision, sha256 = version.PayloadSha256Hex,
            profile = compact.Profile, slot = Slot, action = "add", includePublished = true, customerScope = "source", previewToken = token };
        using var previewResponse = await client.PostAsJsonAsync(endpoint + "/preview", Input()); previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("canApply").GetBoolean()); Assert.Equal(1, preview.GetProperty("updatedDrafts").GetInt32());
        Assert.Equal("excluded", Row(preview, wrong).GetProperty("status").GetString()); Assert.Equal("excluded", Row(preview, ambiguous).GetProperty("status").GetString());
        using var applied = await client.PostAsJsonAsync(endpoint, Input(Token(preview))); applied.EnsureSuccessStatusCode();
        await using var readScope = host.Services.CreateAsyncScope();
        var database = readScope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var ids = new[] { target.Id, wrong.Id, ambiguous.Id };
        var rows = await database.TableVersions.AsNoTracking().Include(row => row.TablePayload).Where(row => ids.Contains(row.TableSetId)).ToDictionaryAsync(row => row.TableSetId);
        var expected = compactBytes.ToArray(); sourceBytes.AsSpan(offset + 4, 8).CopyTo(expected.AsSpan(offset + 4));
        Assert.Equal(expected, TableDistributionService.GetEffectivePayload(rows[target.Id]));
        Assert.Equal(compact.Profile, rows[target.Id].ScreeningLayoutProfile);
        Assert.Equal(wrong.Versions.Single().EmbeddedPayload, TableDistributionService.GetEffectivePayload(rows[wrong.Id]));
        Assert.Equal(ambiguous.Versions.Single().EmbeddedPayload, TableDistributionService.GetEffectivePayload(rows[ambiguous.Id]));
    }

    private static WebApplicationFactory<Program> Factory(NccFactory ncc, string key) => ncc.WithWebHostBuilder(web => web.UseEnvironment("Production")
        .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
        .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
        .UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
    private static OperatorAccount Account(string password, Dictionary<string, int> permissions) => new() { Username = "fixture-" + Guid.NewGuid().ToString("N"),
        DisplayName = "Fixture operator", Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(permissions) };
    private static Customer Customer() => new() { Name = "Fixture screening customer", Code = Guid.NewGuid().ToString("N")[..8] };
    private static Site Site(Customer customer) => new() { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
    private static Terminal Phone(Site site) => new() { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
    private static TableSet Set(Customer owner, TableDefinition definition, byte[] bytes, string name, bool published = false, Dictionary<int, string>? descriptions = null) =>
        new() { Customer = owner, Name = name + " " + Guid.NewGuid().ToString("N")[..8], Status = published ? TableSetStatus.Published : TableSetStatus.Draft,
            Versions = [Version(definition, bytes, 1, descriptions)] };
    private static TableVersion Version(TableDefinition definition, byte[] bytes, int order, Dictionary<int, string>? descriptions = null) =>
        new() { TableDefinition = definition, EmbeddedPayload = bytes, SortOrder = order, TableRevision = 1,
            PayloadSha256Hex = TablePayloadHasher.Sha256Hex(bytes), ScreeningDescriptionsJson = descriptions == null ? null : JsonSerializer.Serialize(descriptions) };
    private static byte[] Blank(byte reserved)
    {
        var bytes = NativeTableLayouts.CreateDraftTemplate(Layout); bytes[Offset + 12] = reserved; bytes[Offset + 13] = (byte)(reserved & 0xFC);
        bytes[Offset + 15] = reserved; return bytes;
    }
    private static byte[] Entry(string pattern, byte reserved) => Layout.Apply(Blank(reserved), new Dictionary<string, string>
        { [$"rule-{Slot - 1}.pattern"] = pattern, [$"rule-{Slot - 1}.free"] = "true", [$"rule-{Slot - 1}.answer"] = "true" });
    private static byte[] ExpectedCopy(byte[] source, byte[] target)
    {
        var expected = target.ToArray(); source.AsSpan(Offset, 12).CopyTo(expected.AsSpan(Offset));
        expected[Offset + 13] = (byte)((target[Offset + 13] & 0xFC) | (source[Offset + 13] & 3)); return expected;
    }
    private static string Endpoint(TableSet set) => $"/api/tables/sets/{set.Id}/records/92/screening-bulk";
    private static object Request(TableSet source, string action, bool includePublished = true, string scope = "source", string? token = null)
    {
        var row = source.Versions.Single(version => version.TableDefinition!.TableNumber == 92);
        return new { revision = row.TableRevision, sha256 = row.PayloadSha256Hex, profile = Layout.Profile, slot = Slot, action, includePublished, customerScope = scope, previewToken = token };
    }
    private static async Task<JsonElement> Preview(HttpClient client, TableSet source, string action, bool includePublished = true, string scope = "source")
    { using var response = await client.PostAsJsonAsync(Endpoint(source) + "/preview", Request(source, action, includePublished, scope)); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static string Token(JsonElement preview) => preview.GetProperty("previewToken").GetString()!;
    private static JsonElement Row(JsonElement preview, TableSet set) => preview.GetProperty("targets").EnumerateArray().Single(row => row.GetProperty("setId").GetGuid() == set.Id);
    private static async Task<byte[]> ReadBytes(IServiceProvider services, Guid setId)
    {
        await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        return TableDistributionService.GetEffectivePayload(await db.TableVersions.AsNoTracking().Include(row => row.TablePayload)
            .SingleAsync(row => row.TableSetId == setId && row.TableDefinition!.TableNumber == 92));
    }
    private static async Task AssertBytes(IServiceProvider services, Guid setId, byte[] expected) => Assert.Equal(expected, await ReadBytes(services, setId));
    private static async Task EditSlot(HttpClient client, TableSet set, string action)
    {
        var path = $"/api/tables/sets/{set.Id}/records/92/fields"; var fields = await client.GetFromJsonAsync<JsonElement>(path);
        using var saved = await client.PutAsJsonAsync(path, new { revision = fields.GetProperty("revision").GetInt32(), sha256 = fields.GetProperty("sha256").GetString(),
            profile = Layout.Profile, changes = new Dictionary<string, string>(), screening = new { actions = new[] { new { slot = Slot, action } } } }); saved.EnsureSuccessStatusCode();
    }
    private static async Task EditOtherSlot(HttpClient client, TableSet set)
    {
        var path = $"/api/tables/sets/{set.Id}/records/92/fields"; var fields = await client.GetFromJsonAsync<JsonElement>(path);
        using var saved = await client.PutAsJsonAsync(path, new { revision = fields.GetProperty("revision").GetInt32(), sha256 = fields.GetProperty("sha256").GetString(),
            profile = Layout.Profile, changes = new Dictionary<string, string> { ["rule-2.pattern"] = "3335550128" } }); saved.EnsureSuccessStatusCode();
    }
    private static async Task SignIn(HttpClient client, OperatorAccount account, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = account.Username, password }); login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
    }
    private static async Task Transfer(int port, string wireId, byte[] expected)
    {
        var wire = Convert.FromHexString(wireId);
        using var tcp = new TcpClient(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await tcp.ConnectAsync(IPAddress.Loopback, port, deadline.Token); var stream = tcp.GetStream();
        await stream.WriteAsync(Packet(0, [44, 1]), deadline.Token); AssertAck(await Read());
        var header = await Read(); Assert.Equal(new byte[] { 18 }, header.Data); await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)), deadline.Token);
        var received = new List<byte>();
        while (received.Count < expected.Length)
        {
            var next = await Read(); received.AddRange(next.Data); await stream.WriteAsync(Packet((byte)(NccControl.Ack | next.Control)), deadline.Token);
        }
        Assert.Equal(expected, received);
        await stream.WriteAsync(Packet(1, [14, 92]), deadline.Token); AssertAck(await Read());
        var end = await Read(); Assert.Equal(new byte[] { 13 }, end.Data); await stream.WriteAsync(Packet((byte)(NccControl.Ack | NccControl.Clr | end.Control)), deadline.Token);
        byte[] Packet(byte control, byte[]? data = null) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] });
        static void AssertAck(NccWirePacket packet) { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }
        async Task<NccWirePacket> Read()
        {
            var head = new byte[3]; await stream.ReadExactlyAsync(head, deadline.Token);
            var raw = new byte[NccFrameLayout.GetExpectedWireLength(head[2])]; head.CopyTo(raw, 0); await stream.ReadExactlyAsync(raw.AsMemory(3), deadline.Token);
            var decoded = NccFrameCodec.TryDecode(raw, NccParseMode.Strict); Assert.True(decoded.Success); return decoded.Packet!;
        }
    }
}
