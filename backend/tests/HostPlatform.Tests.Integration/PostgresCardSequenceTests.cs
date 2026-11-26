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
public sealed class PostgresCardSequenceTests
{
    [PostgresIntegrationFact]
    public async Task Position_changes_save_both_records_atomically_and_reject_stale_or_occupied_moves()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var read = Account(1);
        var write = Account(3);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(read, write);
            await db.SaveChangesAsync();
        }

        foreach (var number in new[] { 22, 134 })
        {
            var carrierNumber = CardSequenceWire.CarrierNumber(number);
            var cardDefinition = new TableDefinition { Name = "Card position fixture", TableNumber = number };
            var carrierDefinition = new TableDefinition { Name = "Carrier acceptance fixture", TableNumber = carrierNumber };
            var cards = Template(number);
            var carriers = Template(carrierNumber);
            var width = number == 22 ? 33 : 36;
            var header = number == 22 ? 4 : 10;
            cards[1 + 2] = 2; cards[1 + 5] = 3; cards[1 + 6] = 1; cards[1 + 31] = 7;
            cards[1 + width + 6] = 1; // Occupied position 2.
            cards[1 + 8 * width + 18] = 0xA5; // Distinct byte in inactive position 9.
            carriers[header] = 7;
            carriers[header + 3] = 0xB1;
            carriers[header + 4] = 0x20;

            var draft = Set("Card sequence draft");
            var published = Set("Card sequence published");
            TableDistributionService.MarkPublished(published, DateTime.UtcNow);
            var missing = Set("Card sequence missing companion", false);
            var racing = Set("Card sequence concurrent fixture");
            var staleCarrier = Set("Card sequence stale companion fixture");
            var sameAcceptance = Set("Card sequence unchanged companion fixture");
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                db.AddRange(cardDefinition, carrierDefinition, draft, published, missing, racing, staleCarrier, sameAcceptance);
                await db.SaveChangesAsync();
            }

            var path = Path(draft.Id);
            await SignIn(read);
            var available = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.True(available.GetProperty("available").GetBoolean());
            Assert.Equal(number == 22 ? 20 : 32, available.GetProperty("slots").GetArrayLength());
            Assert.True(available.GetProperty("slots")[8].GetProperty("blank").GetBoolean());
            Assert.False(available.GetProperty("slots")[1].GetProperty("blank").GetBoolean());
            using (var denied = await client.PostAsJsonAsync(path, Move(9))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            await SignIn(write);
            using (var occupied = await client.PostAsJsonAsync(path, Move(2))) Assert.Equal(HttpStatusCode.BadRequest, occupied.StatusCode);
            using (var locked = await client.PostAsJsonAsync(Path(published.Id), Move(9))) Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
            Assert.False((await client.GetFromJsonAsync<JsonElement>(Path(missing.Id))).GetProperty("available").GetBoolean());
            using (var incomplete = await client.PostAsJsonAsync(Path(missing.Id), Move(9))) Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
            using (var moved = await client.PostAsJsonAsync(path, Move(9)))
            {
                Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
                var result = await moved.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(result.GetProperty("carrierChanged").GetBoolean());
                Assert.Equal(2, result.GetProperty("cards").GetProperty("revision").GetInt32());
                Assert.Equal(2, result.GetProperty("carriers").GetProperty("revision").GetInt32());
            }
            using (var stale = await client.PostAsJsonAsync(path, Move(10))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var expected = CardSequenceWire.Move(number, cards, carriers, 1, 9);
            await AssertSaved(draft.Id, expected, 2);
            await AssertSaved(published.Id, new CardSequenceWire.MoveResult(cards, carriers), 1);

            // Source and position 5 have the same acceptance in every carrier. The card still moves,
            // but no new carrier revision is needed when its complete payload remains unchanged.
            using (var moved = await client.PostAsJsonAsync(Path(sameAcceptance.Id), Move(5)))
            {
                Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
                var result = await moved.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(result.GetProperty("carrierChanged").GetBoolean());
                Assert.Equal(1, result.GetProperty("carriers").GetProperty("revision").GetInt32());
            }
            await AssertSaved(sameAcceptance.Id, CardSequenceWire.Move(number, cards, carriers, 1, 5), 2, 1);

            // A stale carrier stamp alone invalidates the request and cannot change either record.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<TableDistributionService>();
                var record = await db.TableVersions.SingleAsync(row => row.TableSetId == staleCarrier.Id && row.TableDefinitionId == carrierDefinition.Id);
                var changed = carriers.ToArray(); changed[header + 4] ^= 0x80;
                await service.UpsertVersionAsync(staleCarrier.Id, carrierDefinition.Id, 2, changed, record.SortOrder, null, CancellationToken.None);
            }
            using (var stale = await client.PostAsJsonAsync(Path(staleCarrier.Id), Move(9))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var record = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == staleCarrier.Id && row.TableDefinitionId == cardDefinition.Id);
                Assert.Equal(cards, TableDistributionService.GetEffectivePayload(record));
                Assert.Equal(1, record.TableRevision);
            }

            // Two competing moves from the same observed pair can commit only one pair.
            var attempts = await Task.WhenAll(client.PostAsJsonAsync(Path(racing.Id), Move(9)), client.PostAsJsonAsync(Path(racing.Id), Move(10)));
            try
            {
                Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.OK);
                Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
                var destination = attempts[0].StatusCode == HttpStatusCode.OK ? 9 : 10;
                await AssertSaved(racing.Id, CardSequenceWire.Move(number, cards, carriers, 1, destination), 2);
            }
            finally { foreach (var response in attempts) response.Dispose(); }

            string Path(Guid id) => $"/api/tables/sets/{id}/records/{number}/card-sequence";
            object Move(int to) => new { from = 1, to, cards = new { revision = 1, sha256 = Hash(cards) },
                carriers = new { revision = 1, sha256 = Hash(carriers) } };
            TableSet Set(string name, bool includeCarrier = true)
            {
                var set = new TableSet { Name = name };
                set.Versions.Add(Version(cardDefinition, cards, 1));
                if (includeCarrier) set.Versions.Add(Version(carrierDefinition, carriers, 2));
                return set;
            }
            TableVersion Version(TableDefinition definition, byte[] bytes, int sort) => new()
            {
                TableDefinition = definition, EmbeddedPayload = bytes.ToArray(), TableRevision = 1,
                PayloadSha256Hex = Hash(bytes), SortOrder = sort, ConfigurationId = "LAB"
            };
            async Task AssertSaved(Guid id, CardSequenceWire.MoveResult payloads, int revision, int? carrierRevision = null)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var records = await db.TableVersions.Include(row => row.TablePayload).Where(row => row.TableSetId == id).OrderBy(row => row.SortOrder).ToArrayAsync();
                Assert.Equal(2, records.Length);
                Assert.Equal(payloads.Cards, TableDistributionService.GetEffectivePayload(records[0]));
                Assert.Equal(payloads.Carriers, TableDistributionService.GetEffectivePayload(records[1]));
                Assert.Equal(revision, records[0].TableRevision);
                Assert.Equal(carrierRevision ?? revision, records[1].TableRevision);
                Assert.All(records, record => Assert.Equal("LAB", record.ConfigurationId));
                Assert.Equal(Hash(payloads.Cards), records[0].PayloadSha256Hex);
                Assert.Equal(Hash(payloads.Carriers), records[1].PayloadSha256Hex);
                if (revision == 2)
                    Assert.Single(await db.AuditEvents.Where(row => row.Action == "move_card_entry" && row.Resource == $"TableSet:{id}").ToListAsync());
            }
        }

        static byte[] Template(int number) => NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(number)));
        static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        OperatorAccount Account(int rights) => new()
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator", Role = OperatorRole.Operator,
            PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["table-records"] = rights })
        };
        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { account.Username, password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
    }
}
