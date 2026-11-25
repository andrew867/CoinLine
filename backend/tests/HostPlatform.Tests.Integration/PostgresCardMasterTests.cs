using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresCardMasterTests
{
    [PostgresIntegrationFact]
    public async Task Master_membership_moves_copies_refreshes_and_reports_affected_phones_without_mutating_published_rows()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var read = new OperatorAccount { Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator",
            Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int>
            { ["card-master"] = 1, ["table-records"] = 1, ["table-sets"] = 1, ["table-assignments"] = 1 }) };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.OperatorAccounts.Add(read); await db.SaveChangesAsync();
        }

        foreach (var number in new[] { 22, 134 })
        {
            var carrierNumber = CardSequenceWire.CarrierNumber(number);
            var code = "M" + Guid.NewGuid().ToString("N")[..3];
            var changes = new Dictionary<string, string> { ["card-0.format"] = "1", ["card-0.start"] = "000001", ["card-0.end"] = "000003",
                ["card-0.reference"] = "17", ["card-0.carrier"] = "7" };
            if (number == 134)
            {
                changes["card-0.spare.control.bit7"] = "true";
                changes["card-0.spare.spillTerminator.bit7"] = "true";
            }
            using var created = await client.PostAsJsonAsync("/api/card-master", new { code, name = "Synthetic master card", tableNumber = number, changes });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var detail = await created.Content.ReadFromJsonAsync<JsonElement>();
            if (number == 134)
            {
                var spare = detail.GetProperty("sections").EnumerateArray()
                    .SelectMany(section => section.GetProperty("fields").EnumerateArray())
                    .Single(field => field.GetProperty("key").GetString() == "card-0.spare.control.bit7");
                Assert.True(spare.GetProperty("isSpare").GetBoolean());
                Assert.Equal("true", spare.GetProperty("value").GetString());
            }
            var master = detail.GetProperty("entry");
            var masterId = master.GetProperty("id").GetGuid();
            var rev = master.GetProperty("revision").GetInt32();
            var masterRow = CardMasterWire.Edit(number, CardMasterWire.Empty(number), changes);
            using (var noOp = await client.PutAsJsonAsync($"/api/card-master/{masterId}", new { code, name = "Synthetic master card", tableNumber = number, revision = rev, changes = new Dictionary<string, string>() }))
            {
                noOp.EnsureSuccessStatusCode();
                Assert.Equal(rev, (await noOp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("revision").GetInt32());
            }

            var localMapping = CardMasterWire.Mapping(number, masterRow).ToDictionary(pair => pair.Key, pair => pair.Value);
            localMapping["card-0.carrier"] = "8";
            var localRow = CardMasterWire.Map(number, masterRow, localMapping);
            var cards = CardMasterWire.Expand(number, localRow);
            var carriers = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(carrierNumber).Single());
            var header = number == 22 ? 4 : 10;
            carriers[header] = 8; carriers[header + 3] = 1;
            var cardDef = new TableDefinition { Name = "Synthetic card fixture", TableNumber = number };
            var carrierDef = new TableDefinition { Name = "Synthetic carrier fixture", TableNumber = carrierNumber };
            var draft = new TableSet { Name = "Master list configuration fixture" };
            draft.Versions.Add(Version(cardDef, cards, 1)); draft.Versions.Add(Version(carrierDef, carriers, 2));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.AddRange(cardDef, carrierDef, draft); await db.SaveChangesAsync();
                var saved = await db.CardMasterDefinitions.SingleAsync(row => row.Id == masterId); Assert.Equal(masterRow, saved.RowPayload);
            }
            var path = Path(draft.Id);
            var snapshot = await Read(draft.Id);
            var sourceCode = "S" + Guid.NewGuid().ToString("N")[..3];
            using (var captured = await client.PostAsJsonAsync("/api/card-master", new { code = sourceCode, name = "Captured synthetic row", tableNumber = number,
                changes = new Dictionary<string, string>(), source = new { setId = draft.Id, position = 1, revision = 1, sha256 = snapshot.GetProperty("cards").GetProperty("sha256").GetString() } }))
            {
                captured.EnsureSuccessStatusCode(); var savedId = (await captured.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry").GetProperty("id").GetGuid();
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                Assert.Equal(localRow, (await db.CardMasterDefinitions.SingleAsync(row => row.Id == savedId)).RowPayload);
            }
            await SignIn(read);
            using (var denied = await client.PostAsJsonAsync(path + "/add", AddRequest(snapshot))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
            using (var missingMapping = await client.PostAsJsonAsync(path + "/add", new { definitionId = masterId, definitionRevision = rev, cards = snapshot.GetProperty("cards"), carriers = snapshot.GetProperty("carriers") }))
                Assert.Equal(HttpStatusCode.BadRequest, missingMapping.StatusCode);
            using (var added = await client.PostAsJsonAsync(path + "/add", AddRequest(snapshot)))
            {
                added.EnsureSuccessStatusCode(); Assert.Equal(2, (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("position").GetInt32());
            }
            var expected = CardMasterWire.Add(number, cards, carriers, masterRow, localMapping, [8]);
            await Check(draft.Id, expected.Cards, expected.Carriers, new[] { 2 }, rev);

            snapshot = await Read(draft.Id);
            using (var linked = await client.PostAsJsonAsync(path + "/link", new { position = 1, definitionId = masterId, definitionRevision = rev,
                cards = snapshot.GetProperty("cards"), carriers = snapshot.GetProperty("carriers") })) linked.EnsureSuccessStatusCode();
            await Check(draft.Id, expected.Cards, expected.Carriers, new[] { 1, 2 }, rev);

            snapshot = await Read(draft.Id);
            using (var moved = await client.PostAsJsonAsync(path, new { from = 2, to = 9, cards = snapshot.GetProperty("cards"), carriers = snapshot.GetProperty("carriers") })) moved.EnsureSuccessStatusCode();
            var movedExpected = CardSequenceWire.Move(number, expected.Cards, expected.Carriers, 2, 9);
            await Check(draft.Id, movedExpected.Cards, movedExpected.Carriers, new[] { 1, 9 }, rev);
            using var cloned = await client.PostAsJsonAsync($"/api/tables/sets/{draft.Id}/clone", new { name = "Master list draft copy fixture" });
            cloned.EnsureSuccessStatusCode();
            var clone = (await cloned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await Check(clone, movedExpected.Cards, movedExpected.Carriers, new[] { 1, 9 }, rev);
            using var destinationResponse = await client.PostAsJsonAsync("/api/tables/sets", new { name = "Master selected record copy" });
            destinationResponse.EnsureSuccessStatusCode();
            var destination = (await destinationResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var sourceDetail = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{draft.Id}");
            var destinationDetail = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{destination}");
            var copyInput = new { sourceSetId = draft.Id, sourceRevision = sourceDetail.GetProperty("revision").GetInt32(),
                destinationRevision = destinationDetail.GetProperty("revision").GetInt32(), recordNumbers = new[] { number, carrierNumber } };
            using (var copied = await client.PostAsJsonAsync($"/api/tables/sets/{destination}/record-copy", copyInput)) copied.EnsureSuccessStatusCode();
            await Check(destination, movedExpected.Cards, movedExpected.Carriers, new[] { 1, 9 }, rev);
            destinationDetail = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{destination}");
            using (var unchangedCopy = await client.PostAsJsonAsync($"/api/tables/sets/{destination}/record-copy", new
            { copyInput.sourceSetId, copyInput.sourceRevision, destinationRevision = destinationDetail.GetProperty("revision").GetInt32(), copyInput.recordNumbers }))
            {
                unchangedCopy.EnsureSuccessStatusCode();
                Assert.All((await unchangedCopy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("records").EnumerateArray(), row => Assert.Equal("keep", row.GetProperty("action").GetString()));
            }

            // A direct edit detaches only the edited slot, not the other linked entry.
            var fieldsPath = $"/api/tables/sets/{clone}/records/{number}/fields";
            var fields = await client.GetFromJsonAsync<JsonElement>(fieldsPath);
            var directChanges = number == 134 ? new Dictionary<string, string> { ["card-0.spare.control.bit7"] = "false" }
                : new Dictionary<string, string> { ["card-0.reference"] = "19" };
            using (var edited = await client.PutAsJsonAsync(fieldsPath, new { revision = fields.GetProperty("revision").GetInt32(),
                sha256 = fields.GetProperty("sha256").GetString(), profile = fields.GetProperty("profile").GetString(),
                changes = directChanges })) edited.EnsureSuccessStatusCode();
            var editedCards = movedExpected.Cards.ToArray();
            if (number == 134) editedCards[34] &= 0x7F;
            else editedCards[1 + 31] = 19;
            await Check(clone, editedCards, movedExpected.Carriers, new[] { 9 }, rev);

            using (var published = await client.PostAsync($"/api/tables/sets/{draft.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();
            Guid phoneId;
            Guid skippedPhoneId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var customer = new Customer { Name = "Synthetic master fixture", Code = Guid.NewGuid().ToString("N")[..8] };
                var site = new Site { CustomerId = customer.Id, Name = "Synthetic test site", Code = "BENCH" };
                var phone = new Terminal { SiteId = site.Id, TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), DisplayName = "Synthetic test terminal" };
                var skippedPhone = new Terminal { SiteId = site.Id, TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), DisplayName = "Synthetic inactive terminal", DeactivatedAtUtc = DateTime.UtcNow };
                phoneId = phone.Id; skippedPhoneId = skippedPhone.Id;
                db.AddRange(customer, site, phone, skippedPhone, new TerminalTableAssignment { TerminalId = phone.Id, TableSetId = draft.Id, SiteId = site.Id, CustomerId = customer.Id },
                    new TerminalTableAssignment { TerminalId = skippedPhone.Id, TableSetId = draft.Id, SiteId = site.Id, CustomerId = customer.Id });
                await db.SaveChangesAsync();
            }
            var revisedChanges = new Dictionary<string, string> { ["card-0.reference"] = "18", ["card-0.carrier"] = "9" };
            if (number == 134) revisedChanges["card-0.spare.spillTerminator.bit7"] = "false";
            using (var revised = await client.PutAsJsonAsync($"/api/card-master/{masterId}", new { code, name = "Synthetic master card", tableNumber = number, revision = rev,
                changes = revisedChanges })) revised.EnsureSuccessStatusCode();
            var usage = await client.GetFromJsonAsync<JsonElement>($"/api/card-master/{masterId}/usage");
            Assert.Equal(2, usage.GetProperty("outdatedAssignedPhones").GetInt32());
            Assert.Equal(3, usage.GetProperty("configurations").GetArrayLength());
            await Check(draft.Id, movedExpected.Cards, movedExpected.Carriers, new[] { 1, 9 }, rev);
            var newMaster = CardMasterWire.Edit(number, masterRow, revisedChanges);

            var rolloutPath = $"/api/card-master/{masterId}/rollout";
            await SignIn(read);
            using (var denied = await client.PostAsJsonAsync(rolloutPath, new { revision = rev + 1, previewToken = "denied" })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
            using var previewResponse = await client.PostAsJsonAsync(rolloutPath + "/preview", new { revision = rev + 1 });
            previewResponse.EnsureSuccessStatusCode(); var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, preview.GetProperty("eligiblePhones").GetInt32()); Assert.Equal(1, preview.GetProperty("scheduledPhones").GetInt32());
            Assert.Equal(1, preview.GetProperty("skippedPhones").GetInt32());
            var token = preview.GetProperty("previewToken").GetString();
            using (var stale = await client.PostAsJsonAsync(rolloutPath, new { revision = rev + 1, previewToken = "stale" })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var attempts = await Task.WhenAll(client.PostAsJsonAsync(rolloutPath, new { revision = rev + 1, previewToken = token }),
                client.PostAsJsonAsync(rolloutPath, new { revision = rev + 1, previewToken = token }));
            JsonElement rollout;
            try
            {
                var applied = Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.OK);
                Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
                rollout = await applied.Content.ReadFromJsonAsync<JsonElement>();
            }
            finally { foreach (var response in attempts) response.Dispose(); }
            Assert.Equal(1, rollout.GetProperty("affectedPhones").GetInt32()); Assert.Equal(1, rollout.GetProperty("scheduledPhones").GetInt32());
            var newSet = rollout.GetProperty("sets")[0].GetProperty("id").GetGuid();
            var firstRefresh = CardMasterWire.Refresh(number, movedExpected.Cards, movedExpected.Carriers, 1, newMaster);
            var bothRefresh = CardMasterWire.Refresh(number, firstRefresh.Cards, firstRefresh.Carriers, 9, newMaster);
            await Check(newSet, bothRefresh.Cards, bothRefresh.Carriers, new[] { 1, 9 }, rev + 1);
            await Check(draft.Id, movedExpected.Cards, movedExpected.Carriers, new[] { 1, 9 }, rev);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var assignment = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phoneId);
                Assert.Equal(newSet, assignment.TableSetId); Assert.Equal(draft.Id, assignment.PreviousTableSetId);
                Assert.Equal(draft.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == skippedPhoneId)).TableSetId);
                var schedule = await db.ScheduledTableDownloads.Include(row => row.LastBatch).ThenInclude(row => row!.Items).SingleAsync(row => row.TerminalId == phoneId);
                Assert.Equal(newSet, schedule.TableSetId); Assert.Equal(new[] { number }, schedule.RecordNumbers);
                Assert.NotNull(schedule.PreparedAtUtc); Assert.Null(schedule.CompletedAtUtc);
                Assert.Single(schedule.LastBatch!.Items);
            }
            using (var replay = await client.PostAsJsonAsync(rolloutPath, new { revision = rev + 1, previewToken = token })) Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

            var cloneSnapshot = await Read(clone);
            using (var stale = await client.PostAsJsonAsync(Path(clone) + "/refresh", new { position = 9, definitionRevision = rev,
                cards = cloneSnapshot.GetProperty("cards"), carriers = cloneSnapshot.GetProperty("carriers") })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using (var refreshed = await client.PostAsJsonAsync(Path(clone) + "/refresh", new { position = 9, definitionRevision = rev + 1,
                cards = cloneSnapshot.GetProperty("cards"), carriers = cloneSnapshot.GetProperty("carriers") })) refreshed.EnsureSuccessStatusCode();
            var refreshedExpected = CardMasterWire.Refresh(number, editedCards, movedExpected.Carriers, 9, newMaster);
            await Check(clone, refreshedExpected.Cards, refreshedExpected.Carriers, new[] { 9 }, rev + 1);
            var same = await Read(clone);
            using (var refreshed = await client.PostAsJsonAsync(Path(clone) + "/refresh", new { position = 9, definitionRevision = rev + 1,
                cards = same.GetProperty("cards"), carriers = same.GetProperty("carriers") }))
            {
                refreshed.EnsureSuccessStatusCode(); var result = await refreshed.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(same.GetProperty("cards").GetProperty("revision").GetInt32(), result.GetProperty("cards").GetProperty("revision").GetInt32());
            }
            cloneSnapshot = await Read(clone);
            using (var removed = await client.PostAsJsonAsync(Path(clone) + "/remove", new { position = 9, confirm = true,
                cards = cloneSnapshot.GetProperty("cards"), carriers = cloneSnapshot.GetProperty("carriers") })) removed.EnsureSuccessStatusCode();
            var removedExpected = CardMasterWire.Remove(number, refreshedExpected.Cards, refreshedExpected.Carriers, 9);
            await Check(clone, removedExpected.Cards, removedExpected.Carriers, [], rev + 1);
            Assert.False((await client.GetFromJsonAsync<JsonElement>($"/api/card-master/{masterId}")).GetProperty("entry").GetProperty("archived").GetBoolean());
            using (var archived = await client.DeleteAsync($"/api/card-master/{masterId}?revision={rev + 1}&confirm=true")) Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
            await Check(draft.Id, movedExpected.Cards, movedExpected.Carriers, new[] { 1, 9 }, rev);
            Assert.True((await client.GetFromJsonAsync<JsonElement>($"/api/card-master/{masterId}")).GetProperty("entry").GetProperty("archived").GetBoolean());

            string Path(Guid set) => $"/api/tables/sets/{set}/records/{number}/card-sequence";
            Task<JsonElement> Read(Guid set) => client.GetFromJsonAsync<JsonElement>(Path(set));
            object AddRequest(JsonElement state) => new { definitionId = masterId, definitionRevision = rev, mapping = localMapping, acceptedCarriers = new[] { 8 },
                cards = state.GetProperty("cards"), carriers = state.GetProperty("carriers") };
            async Task Check(Guid set, byte[] expectedCards, byte[] expectedCarriers, int[] positions, int revision)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var records = await db.TableVersions.Include(row => row.TablePayload).Where(row => row.TableSetId == set).OrderBy(row => row.SortOrder).ToArrayAsync();
                Assert.Equal(2, records.Length);
                Assert.Equal(expectedCards, TableDistributionService.GetEffectivePayload(records[0]));
                Assert.Equal(expectedCarriers, TableDistributionService.GetEffectivePayload(records[1]));
                var links = CardMasterLinks.Read(records[0].CardMasterLinksJson);
                Assert.Equal(positions, links.Keys.ToArray());
                Assert.All(links.Values, link => { Assert.Equal(masterId, link.DefinitionId); Assert.Equal(revision, link.Revision); });
            }
        }

        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { account.Username, password }); login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
        static TableVersion Version(TableDefinition definition, byte[] bytes, int sort) => new()
        {
            TableDefinition = definition, EmbeddedPayload = bytes, TableRevision = 1,
            PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), SortOrder = sort
        };
    }
}
