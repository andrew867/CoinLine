using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresCraftAccessConfigurationTests
{
    [PostgresIntegrationFact]
    public async Task Only_admin_can_explicitly_view_the_saved_code_blank_preserves_and_replacement_is_revision_bound()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var operatorAccount = TestOperatorAuthentication.EnsureAccount(factory, $"craft-operator-{Guid.NewGuid():N}", "Operator");
        using var operatorClient = TestOperatorAuthentication.CreateAuthenticatedClient(factory, operatorAccount.Username, "Operator");
        using var anonymous = factory.CreateClient();
        foreach (var profile in new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.LaterMultiPay180, FirmwareTableProfiles.GraphicalMultiPay180 })
        {
            var definition = new TableDefinition { Name = "Synthetic install settings", TableNumber = 31 };
            var firmware = new FirmwareVersion { Label = "Synthetic install target", TableProfileId = profile };
            var layout = NativeTableLayouts.Candidates(31, new byte[] { 31 }.Concat(new byte[36]).ToArray())
                .Single(row => row.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(31));
            var source = new byte[37]; source[0] = 31; source[11] = 0xA5; source[36] = 0x5A;
            var code = Digits(7); var replacement = Digits(7); while (replacement == code) replacement = Digits(7);
            var keyCard = Digits(10);
            var original = layout.Apply(source, new Dictionary<string, string> { ["craftAccessCode"] = code, ["keyCardNumber"] = keyCard });
            var record = CallSetupSpareFixture.Version(definition, original, 1);
            var set = new TableSet { Name = "Synthetic install settings", TargetFirmwareVersion = firmware };
            set.Versions.Add(record);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
                db.AddRange(firmware, definition, set);
                (await db.OperatorAccounts.SingleAsync(row => row.Id == operatorAccount.Id)).PermissionProfileJson = "{\"table-records\":3}";
                await db.SaveChangesAsync();
            }
            var path = $"/api/tables/sets/{set.Id}/records/31/fields";
            var revealPath = path + "/craft-access";
            var snapshot = await ReadRedacted();
            using (var denied = await anonymous.PostAsJsonAsync(revealPath, Stamp(snapshot))) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using (var denied = await operatorClient.PostAsJsonAsync(revealPath, Stamp(snapshot))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            await CheckReveal(snapshot, code);
            using (var blank = await Save(snapshot, new() { ["craftAccessCode"] = "", ["keyCardNumber"] = "" })) blank.EnsureSuccessStatusCode();
            await CheckRecord(1, original);
            using (var bad = await Save(snapshot, new() { ["craftAccessCode"] = new string('0', 6) })) Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            await CheckRecord(1, original);
            using (var edited = await Save(snapshot, new() { ["craftAccessCode"] = replacement })) edited.EnsureSuccessStatusCode();
            var expected = original.ToArray(); layout.Apply(original, new Dictionary<string, string> { ["craftAccessCode"] = replacement }).AsSpan(1, 4).CopyTo(expected.AsSpan(1, 4));
            await CheckRecord(2, expected);
            using (var stale = await admin.PostAsJsonAsync(revealPath, Stamp(snapshot))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var current = await ReadRedacted(); await CheckReveal(current, replacement);
            using (var wrongProfile = await admin.PostAsJsonAsync(revealPath, new { revision = 2, sha256 = current.GetProperty("sha256").GetString(), profile = "unidentified" }))
                Assert.Equal(HttpStatusCode.BadRequest, wrongProfile.StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                (await db.TableSets.SingleAsync(row => row.Id == set.Id)).Status = TableSetStatus.Published;
                await db.SaveChangesAsync();
            }
            var published = await ReadRedacted(); Assert.False(published.GetProperty("editable").GetBoolean());
            await CheckReveal(published, replacement);
            using (var locked = await Save(published, new() { ["craftAccessCode"] = code })) Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);

            // A malformed stored value is not rendered as a plausible credential.
            expected[4] &= 0xF0;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var stored = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == set.Id);
                stored.TablePayloadId = null; stored.TablePayload = null; stored.EmbeddedPayload = expected;
                stored.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(expected); stored.TableRevision++;
                await db.SaveChangesAsync();
            }
            await CheckReveal(await ReadRedacted(), null);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var audits = await db.AuditEvents.Where(row => row.Category == "table.credentials" && row.Resource == set.Id.ToString()).ToListAsync();
                Assert.Equal(4, audits.Count);
                foreach (var audit in audits)
                {
                    Assert.Equal("reveal_craft_access", audit.Action);
                    Assert.DoesNotContain(code, audit.DetailJson); Assert.DoesNotContain(replacement, audit.DetailJson); Assert.DoesNotContain(keyCard, audit.DetailJson);
                    Assert.DoesNotContain("code", audit.DetailJson, StringComparison.OrdinalIgnoreCase);
                }
            }

            async Task<JsonElement> ReadRedacted()
            {
                foreach (var client in new[] { admin, operatorClient })
                {
                    var json = await client.GetStringAsync(path);
                    Assert.DoesNotContain(code, json); Assert.DoesNotContain(replacement, json); Assert.DoesNotContain(keyCard, json);
                    var fields = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("sections").EnumerateArray()
                        .SelectMany(section => section.GetProperty("fields").EnumerateArray()).ToArray();
                    foreach (var key in new[] { "craftAccessCode", "keyCardNumber" })
                        Assert.Equal(JsonValueKind.Null, fields.Single(field => field.GetProperty("key").GetString() == key).GetProperty("value").ValueKind);
                }
                return await admin.GetFromJsonAsync<JsonElement>(path);
            }
            async Task CheckReveal(JsonElement observed, string? value)
            {
                using var response = await admin.PostAsJsonAsync(revealPath, Stamp(observed)); response.EnsureSuccessStatusCode();
                Assert.True(response.Headers.CacheControl?.NoStore);
                var contents = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(value, Assert.Single(contents.EnumerateObject()).Value.GetString());
            }
            Task<HttpResponseMessage> Save(JsonElement observed, Dictionary<string, string> changes) => admin.PutAsJsonAsync(path,
                new { revision = observed.GetProperty("revision").GetInt32(), sha256 = observed.GetProperty("sha256").GetString(), profile = observed.GetProperty("profile").GetString(), changes });
            async Task CheckRecord(int revision, byte[] expected)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var stored = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == set.Id);
                Assert.Equal(revision, stored.TableRevision); Assert.Equal(expected, TableDistributionService.GetEffectivePayload(stored));
            }
        }
        static object Stamp(JsonElement observed) => new { revision = observed.GetProperty("revision").GetInt32(),
            sha256 = observed.GetProperty("sha256").GetString(), profile = observed.GetProperty("profile").GetString() };
        static string Digits(int count) => string.Concat(Enumerable.Range(0, count).Select(_ => RandomNumberGenerator.GetInt32(10).ToString()));
    }
}
