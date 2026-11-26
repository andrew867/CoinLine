using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class DemoConfigurationSeedTests
{
    [ManualDemoBundleFact]
    public void Locally_supplied_bundle_passes_the_same_import_validation()
    {
        var rows = DemoConfigurationSeed.ReadBundle(Environment.GetEnvironmentVariable(ManualDemoBundleFact.Variable)!);
        Assert.Equal(new[] { 12, 27 }, rows.Select(row => row.Records.Count));
    }
    [Fact]
    public void Complete_profile_separated_bundle_requires_exact_records_and_digest()
    {
        var (directory, expected) = Fixture();
        var bundle = DemoConfigurationSeed.ReadBundle(directory);
        Assert.Equal(new[] { 12, 27 }, bundle.Select(row => row.Records.Count));
        Assert.Equal(FirmwareTableProfiles.MultiPay200, bundle[0].ProfileId);
        Assert.Equal(FirmwareTableProfiles.LaterMultiPay180, bundle[1].ProfileId);
        foreach (var starter in bundle)
            foreach (var (number, raw) in starter.Records)
                Assert.Equal(expected[(starter.ProfileId, number)], raw);

        var path = Path.Combine(directory, "verified-demo", "mt026.bin");
        var changed = File.ReadAllBytes(path); changed[2] ^= 1; File.WriteAllBytes(path, changed);
        Assert.Throws<InvalidOperationException>(() => DemoConfigurationSeed.ReadBundle(directory));
        File.WriteAllBytes(path, expected[(FirmwareTableProfiles.MultiPay200, 26)]);
        var manifestPath = Path.Combine(directory, "manifest.json"); var original = File.ReadAllText(manifestPath);
        var manifest = JsonNode.Parse(original)!;
        manifest["entries"]![0]!["file"] = "../mt026.bin";
        Assert.Throws<InvalidOperationException>(() =>
        {
            File.WriteAllText(manifestPath, manifest.ToJsonString()); DemoConfigurationSeed.ReadBundle(directory);
        });
        manifest = JsonNode.Parse(original)!;
        manifest["entries"]!.AsArray().RemoveAt(0);
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => DemoConfigurationSeed.ReadBundle(directory));
        File.WriteAllText(manifestPath, original);
        Assert.Equal(39, DemoConfigurationSeed.ReadBundle(directory).Sum(row => row.Records.Count));
    }

    [PostgresIntegrationFact]
    public async Task Configured_startup_seeds_default_drafts_and_preserves_existing_operator_edits_and_assignments()
    {
        var (directory, expected) = Fixture();
        var connection = Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)!;
        var schema = "coinline_demo_seed_" + Guid.NewGuid().ToString("N");
        await using (var server = new NpgsqlConnection(connection))
        {
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", server);
            await create.ExecuteNonQueryAsync();
        }
        var scoped = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        var target = new FirmwareVersion { Label = "Fixture earlier target", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        await using (var db = new HostPlatformDbContext(new DbContextOptionsBuilder<HostPlatformDbContext>().UseNpgsql(scoped).Options))
        {
            await DatabaseBaseline.MigrateAsync(db);
            db.FirmwareVersions.Add(target); await db.SaveChangesAsync();
        }
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", scoped).UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "true")
            .UseSetting("Platform:Seed:DemoConfigurationBundlePath", directory));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        Guid earlyId, laterId; byte[] corrected;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var sets = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                .Include(row => row.Versions).ThenInclude(row => row.TablePayload)
                .Where(row => row.Name == DemoConfigurationSeed.EarlierName || row.Name == DemoConfigurationSeed.LaterName).ToArrayAsync();
            Assert.Equal(2, sets.Length);
            var early = Assert.Single(sets, row => row.Name == DemoConfigurationSeed.EarlierName);
            var later = Assert.Single(sets, row => row.Name == DemoConfigurationSeed.LaterName);
            earlyId = early.Id; laterId = later.Id;
            Assert.Equal(target.Id, early.TargetFirmwareVersionId); Assert.Null(later.TargetFirmwareVersionId);
            Assert.Equal(12, early.Versions.Count); Assert.Equal(27, later.Versions.Count);
            foreach (var (set, profile) in new[] { (early, FirmwareTableProfiles.MultiPay200), (later, FirmwareTableProfiles.LaterMultiPay180) })
            {
                Assert.True(set.IsDefault); Assert.Equal(TableSetStatus.Draft, set.Status);
                foreach (var version in set.Versions)
                {
                    var raw = expected[(profile, version.TableDefinition!.TableNumber)];
                    Assert.Equal(raw, TableDistributionService.GetEffectivePayload(version));
                    Assert.Equal(Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(), version.PayloadSha256Hex);
                }
            }
            Assert.Empty(await db.TerminalTableAssignments.Where(row => row.TableSetId == earlyId || row.TableSetId == laterId).ToListAsync());
            var row = Assert.Single(early.Versions, version => version.TableDefinition!.TableNumber == 26);
            corrected = TableDistributionService.GetEffectivePayload(row).ToArray(); corrected[1] = 2;
            scope.ServiceProvider.GetRequiredService<TableDistributionService>().StageDraftPayloadReplacement(early, row, corrected);
            await db.SaveChangesAsync();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await DemoConfigurationSeed.EnsureAsync(db, directory);
            Assert.Equal(2, await db.TableSets.CountAsync(row => row.Name == DemoConfigurationSeed.EarlierName || row.Name == DemoConfigurationSeed.LaterName));
            var record = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == earlyId && row.TableDefinition!.TableNumber == 26);
            Assert.Equal(corrected, TableDistributionService.GetEffectivePayload(record)); Assert.Equal(2, record.TableRevision);
            Assert.Empty(await db.DownloadBatches.Where(row => row.TableSetId == earlyId || row.TableSetId == laterId).ToListAsync());
            Assert.Empty(await db.TerminalTableAssignments.Where(row => row.TableSetId == earlyId || row.TableSetId == laterId).ToListAsync());
        }
    }

    private static (string Directory, Dictionary<(string Profile, int Number), byte[]> Expected) Fixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "coinline-demo-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var groups = new[] { ("verified-demo", FirmwareTableProfiles.MultiPay200, new[] { 26,27,28,29,30,32,33,34,35,58 }),
            ("archived-mtr19-demo", FirmwareTableProfiles.MultiPay200, new[] { 50,73 }),
            ("archived-demo", FirmwareTableProfiles.LaterMultiPay180, new[] { 26,30,32,33,34,35,50,73,150,151,152 }.Concat(Enumerable.Range(136,14)).Concat([154,155]).ToArray()) };
        var entries = new List<object>(); var expected = new Dictionary<(string, int), byte[]>();
        foreach (var (group, profile, numbers) in groups)
        {
            Directory.CreateDirectory(Path.Combine(directory, group));
            foreach (var number in numbers)
            {
                var raw = new byte[FirmwareTableProfiles.Find(profile)!.VerifiedLengths[number]]; raw[0] = (byte)number;
                raw[2] = (byte)(profile == FirmwareTableProfiles.MultiPay200 ? 19 : 20);
                expected.Add((profile, number), raw);
                var relative = $"{group}/mt{number:D3}.bin"; File.WriteAllBytes(Path.Combine(directory, relative), raw);
                entries.Add(new { message_type = number, group, file = relative, record_length = raw.Length, payload_length = raw.Length - 1,
                    sha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant() });
            }
        }
        entries.Add(new { file = "draft-only/review.json", group = "review" });
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new { schema_version = 1, entries }));
        return (directory, expected);
    }
}

internal sealed class ManualDemoBundleFact : FactAttribute
{
    public const string Variable = "COINLINE_TEST_DEMO_BUNDLE_DIR";
    public ManualDemoBundleFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable))) Skip = "Optional locally supplied demo starter bundle.";
    }
}
