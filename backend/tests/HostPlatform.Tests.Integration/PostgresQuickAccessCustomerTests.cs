using System.Buffers.Binary;
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
public sealed class PostgresQuickAccessCustomerTests
{
    private const string Path = "/api/quick-access-customers";

    [PostgresIntegrationFact]
    public async Task Directory_owns_unique_references_with_private_details_and_byte_exact_table_editing()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        using var anonymous = factory.CreateClient();
        using (var denied = await anonymous.PostAsJsonAsync(Path + "/lookup", new { })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var lookup = Account(new Dictionary<string, int> { ["quick-access-customer-lookup"] = 1, ["table-records"] = 3 });
        var read = Account(new Dictionary<string, int> { ["quick-access-customers"] = 1 });
        var none = Account(new Dictionary<string, int> { ["table-records"] = 1 });
        string reference;
        string secondReference;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var used = (await db.QuickAccessCustomers.Select(c => c.Reference).ToListAsync()).ToHashSet();
            var available = Enumerable.Range(1, 999999).Select(value => value.ToString("D6")).Where(value => !used.Contains(value)).Take(2).ToArray();
            reference = available[0]; secondReference = available[1];
            db.OperatorAccounts.AddRange(lookup, read, none); await db.SaveChangesAsync();
        }
        var name = "Synthetic directory fixture " + Guid.NewGuid().ToString("N");
        const string confidential = "synthetic-private-comment";
        var input = new { reference, name, billingComments = confidential, contactName = "Synthetic contact", phone = "fixture-phone",
            street = "Synthetic street", city = "Fixture city", province = "Fixture province", postalCode = "Fixture postal", notes = confidential };
        foreach (var invalid in new[] { "000000", "12345", "1234567", "12345A" })
        {
            using var response = await client.PostAsJsonAsync(Path, new { reference = invalid, name });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var invalid = await client.PostAsJsonAsync(Path, new { reference, name = " " })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var invalid = await client.PostAsJsonAsync(Path, new { reference, name, contactName = new string('x', 41) })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var created = await client.PostAsJsonAsync(Path, input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entry = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(reference, entry.GetProperty("reference").GetString());
        Assert.Equal(confidential, entry.GetProperty("billingComments").GetString());
        Assert.Equal("Fixture city", entry.GetProperty("city").GetString());
        Assert.True(created.Headers.CacheControl?.NoStore);
        var id = entry.GetProperty("id").GetGuid();
        var url = Path + "/" + id;
        using (var duplicate = await client.PostAsJsonAsync(Path, input)) Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using (var createdSecond = await client.PostAsJsonAsync(Path, new { reference = secondReference, name = name + " secondary" }))
            Assert.Equal(HttpStatusCode.Created, createdSecond.StatusCode);

        await SignIn(lookup);
        using (var denied = await client.GetAsync(url)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.PostAsJsonAsync(Path + "/search", new { query = name })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.PostAsJsonAsync(Path, input)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var found = await client.PostAsJsonAsync(Path + "/lookup", new { query = name.ToLowerInvariant(), sort = "reference" });
        found.EnsureSuccessStatusCode();
        var results = (await found.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(2, results.Length);
        Assert.Equal(reference, results[0].GetProperty("reference").GetString());
        Assert.Equal(secondReference, results[1].GetProperty("reference").GetString());
        Assert.All(results, row => Assert.Equal(new[] { "name", "reference" }, row.EnumerateObject().Select(p => p.Name).Order().ToArray()));
        var selectedRef = results[0].GetProperty("reference").GetString()!;
        using (var exact = await client.PostAsJsonAsync(Path + "/lookup", new { query = selectedRef }))
        {
            exact.EnsureSuccessStatusCode();
            Assert.Equal(selectedRef, Assert.Single((await exact.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rows").EnumerateArray()).GetProperty("reference").GetString());
        }
        using (var invalid = await client.PostAsJsonAsync(Path + "/lookup", new { page = 0 })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await SignIn(none);
        using (var denied = await client.PostAsJsonAsync(Path + "/lookup", new { query = name })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await SignIn(read);
        using (var details = await client.GetAsync(url))
        {
            details.EnsureSuccessStatusCode();
            var data = await details.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(entry.GetProperty("notes").GetString(), data.GetProperty("notes").GetString());
        }
        using (var searched = await client.PostAsJsonAsync(Path + "/search", new { query = name, sort = "name" }))
        {
            searched.EnsureSuccessStatusCode();
            Assert.Equal(2, (await searched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt32());
        }
        using (var denied = await client.DeleteAsync(url + "?revision=1&confirm=true")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        TestOperatorAuthentication.Authenticate(client, factory);
        var unchangedInput = new { input.reference, input.name, input.billingComments, input.contactName, input.phone,
            input.street, input.city, input.province, input.postalCode, input.notes, revision = 1 };
        using (var unchanged = await client.PutAsJsonAsync(url, unchangedInput))
        {
            unchanged.EnsureSuccessStatusCode(); Assert.Equal(1, (await unchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt32());
        }
        using (var renumbered = await client.PutAsJsonAsync(url, new { reference = secondReference, name, revision = 1 }))
            Assert.Equal(HttpStatusCode.BadRequest, renumbered.StatusCode);
        using (var updated = await client.PutAsJsonAsync(url, new { reference, name, notes = confidential, revision = 1 }))
        {
            updated.EnsureSuccessStatusCode(); Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt32());
        }
        using (var stale = await client.PutAsJsonAsync(url, unchangedInput)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var savedRecords = new Dictionary<Guid, byte[]>();
        foreach (var profile in new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.LaterMultiPay180 })
        {
            var layout = NativeTableLayouts.DraftTemplates(55).Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(55));
            var payload = NativeTableLayouts.CreateDraftTemplate(layout);
            var offset = 1 + 5 * 57 + 51;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), 0x12345678);
            payload[offset - 1] = 0x55; payload[offset + 4] = 0xAF; payload[offset + 5] = 0xA4;
            var expected = payload.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset, 4), uint.Parse(selectedRef, System.Globalization.CultureInfo.InvariantCulture));
            var firmware = new FirmwareVersion { Label = "Synthetic key target", TableProfileId = profile };
            var definition = new TableDefinition { Name = "Synthetic keys", TableNumber = 55 };
            var set = new TableSet { Name = "Synthetic customer key set", TargetFirmwareVersionId = firmware.Id };
            var version = new TableVersion { TableDefinition = definition, EmbeddedPayload = payload, TableRevision = 1,
                PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant() };
            set.Versions.Add(version);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.AddRange(firmware, definition, set); await db.SaveChangesAsync();
            }
            await SignIn(lookup);
            var fields = $"/api/tables/sets/{set.Id}/records/55/fields";
            var original = await client.GetFromJsonAsync<JsonElement>(fields);
            var oldField = original.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray())
                .Single(field => field.GetProperty("key").GetString() == "key-5.customerReference");
            Assert.Equal("305419896", oldField.GetProperty("value").GetString());
            var digest = original.GetProperty("sha256").GetString();
            using (var unchanged = await client.PutAsJsonAsync(fields, new { revision = 1, sha256 = digest, profile = layout.Profile,
                changes = new Dictionary<string, string>() })) unchanged.EnsureSuccessStatusCode();
            Assert.Equal(digest, (await client.GetFromJsonAsync<JsonElement>(fields)).GetProperty("sha256").GetString());
            using (var saved = await client.PutAsJsonAsync(fields, new { revision = 1, sha256 = digest, profile = layout.Profile,
                changes = new Dictionary<string, string> { ["key-5.customerReference"] = uint.Parse(selectedRef).ToString() } }))
                saved.EnsureSuccessStatusCode();
            savedRecords[version.Id] = expected;
            TestOperatorAuthentication.Authenticate(client, factory);
        }
        using (var denied = await client.DeleteAsync(url + "?revision=2")) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using (var archived = await client.DeleteAsync(url + "?revision=2&confirm=true")) Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        using (var duplicate = await client.PostAsJsonAsync(Path, input)) Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using (var hidden = await client.PostAsJsonAsync(Path + "/lookup", new { query = reference, includeArchived = true }))
        {
            hidden.EnsureSuccessStatusCode(); Assert.Empty((await hidden.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rows").EnumerateArray());
        }
        using (var restore = await client.PostAsync(url + "/restore?revision=3", null)) Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        using (var restored = await client.GetAsync(url))
        {
            var data = await restored.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(data.GetProperty("archived").GetBoolean()); Assert.Equal(4, data.GetProperty("revision").GetInt32());
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            foreach (var (versionId, expected) in savedRecords)
            {
                var record = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.Id == versionId);
                Assert.Equal(expected, TableDistributionService.GetEffectivePayload(record));
                Assert.Equal(2, record.TableRevision);
            }
            var audit = await db.AuditEvents.Where(row => row.Category == "quick_access_customer" && row.Resource == id.ToString()).ToListAsync();
            Assert.Equal(4, audit.Count);
            Assert.All(audit, row =>
            {
                Assert.DoesNotContain(confidential, row.DetailJson);
                Assert.DoesNotContain(name, row.DetailJson);
                Assert.DoesNotContain(input.contactName, row.DetailJson);
            });
        }

        OperatorAccount Account(Dictionary<string, int> permissions) => new()
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator", Role = OperatorRole.Operator,
            PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(permissions)
        };
        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { account.Username, password }); login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
    }
}
