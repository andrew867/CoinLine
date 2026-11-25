using System.Net;
using System.Net.Http.Json;
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
public sealed class PostgresAdvertisingGlyphTests
{
    [PostgresIntegrationFact]
    public async Task Advertising_api_round_trips_both_profiles_without_touching_unmapped_cells_or_unspecified_spares()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        foreach (var profile in new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.LaterMultiPay180 })
        {
            var raw = AdvertisingGlyphFixture.Imported();
            var expected = AdvertisingGlyphFixture.Expected(raw);
            var firmware = new FirmwareVersion { Label = "Synthetic text display target", TableProfileId = profile };
            var definition = new TableDefinition { Name = "Synthetic advertising", TableNumber = 29 };
            var set = new TableSet { Name = "Synthetic text display configuration", TargetFirmwareVersionId = firmware.Id };
            var version = AdvertisingGlyphFixture.Version(definition, raw, 1);
            set.Versions.Add(version);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                db.AddRange(firmware, definition, set); await db.SaveChangesAsync();
            }
            var path = $"/api/tables/sets/{set.Id}/advertising";
            var snapshot = await client.GetFromJsonAsync<JsonElement>(path);
            var slots = Slots(snapshot);
            Assert.Equal("A\uFFFD B", slots[0].Text);
            Assert.True(slots[0].ContainsUnmappedGlyphs);
            Assert.Equal(0x90, slots[0].ReservedFlags);
            Assert.Equal(0xA5, slots[0].SpareByte);
            Assert.Equal("Cafe", slots[1].Text);
            Assert.True(slots[2].EndSequence);
            var sha256 = snapshot.GetProperty("sha256").GetString();
            using (var same = await client.PutAsJsonAsync(path, new { slots, revision = 1, sha256 })) same.EnsureSuccessStatusCode();
            await Check(raw, 1);
            var legacySlots = slots.Select(slot => new { slot.Text, slot.DurationTicks, slot.Mode, slot.BlankBefore, slot.EndSequence }).ToArray();
            using (var same = await client.PutAsJsonAsync(path, new { slots = legacySlots, revision = 1, sha256 }))
                same.EnsureSuccessStatusCode();
            await Check(raw, 1);

            var updated = AdvertisingGlyphFixture.Updated(slots);
            var unsupported = updated.ToArray(); unsupported[0] = unsupported[0] with { Text = "A\uFFFD B changed" };
            using (var denied = await client.PutAsJsonAsync(path, new { slots = unsupported, revision = 1, sha256 }))
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            unsupported = updated.ToArray(); unsupported[0] = unsupported[0] with { ReservedFlags = 0x40 };
            using (var denied = await client.PutAsJsonAsync(path, new { slots = unsupported, revision = 1, sha256 }))
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            await Check(raw, 1);
            using (var edited = await client.PutAsJsonAsync(path, new { slots = updated, revision = 1, sha256 })) edited.EnsureSuccessStatusCode();
            await Check(expected, 2);
            using (var stale = await client.PutAsJsonAsync(path, new { slots = updated, revision = 1, sha256 }))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            snapshot = await client.GetFromJsonAsync<JsonElement>(path);
            var after = Slots(snapshot);
            Assert.Equal("Café", after[1].Text);
            Assert.Equal("A\uFFFD B", after[0].Text);
            Assert.Equal(0x20, after[0].ReservedFlags);
            Assert.Equal(0x57, after[0].SpareByte);
            Assert.Equal(3, after[0].Mode); Assert.True(after[0].BlankBefore);
            using (var unchanged = await client.PutAsJsonAsync(path, new { slots = after, revision = 2, sha256 = snapshot.GetProperty("sha256").GetString() }))
                unchanged.EnsureSuccessStatusCode();
            await Check(expected, 2);

            async Task Check(byte[] bytes, int revision)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var row = await db.TableVersions.Include(v => v.TablePayload).SingleAsync(v => v.Id == version.Id);
                Assert.Equal(revision, row.TableRevision);
                Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(row));
            }
        }
    }

    private static AdvertisingSlot[] Slots(JsonElement snapshot) =>
        snapshot.GetProperty("slots").Deserialize<AdvertisingSlot[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
