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
        var counts = rows.Where(row => row.IsDefault).Select(row => row.Records.Count).Order().ToArray();
        Assert.True(counts.SequenceEqual(new[] { 12, 27 }) || counts.SequenceEqual(new[] { 13, 32, 35 }));
    }

    [Fact]
    public void Expanded_bundle_selects_populated_defaults_and_retains_all_reference_payloads()
    {
        var (directory, _, expected) = ExpandedFixture();
        var rows = DemoConfigurationSeed.ReadBundle(directory);
        Assert.Equal(19, rows.Length);
        Assert.Equal(new[] { 13, 32, 35 }, rows.Where(row => row.IsDefault).Select(row => row.Records.Count));
        Assert.Equal(new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.LaterMultiPay180, FirmwareTableProfiles.GraphicalMultiPay180 },
            rows.Where(row => row.IsDefault).Select(row => row.ProfileId));
        var early = Assert.Single(rows, row => row.Name == DemoConfigurationSeed.EarlierName);
        Assert.Equal(expected["archived-mtr19-demo/mt029.bin"], early.Records[29]);
        Assert.Equal(expected["ordinary-routing/mtr19/standard/200-slot/mt092.bin"], early.Records[92]);
        Assert.NotEmpty(AdvertisingTableWire.Read(early.Records[29])[0].Text);
        var standard = Assert.Single(rows, row => row.Name == DemoConfigurationSeed.LaterName);
        Assert.DoesNotContain(standard.Records.Keys, number => number is 98 or 99 or 131 or 132 or 133);
        var graph = Assert.Single(rows, row => row.Name == DemoConfigurationSeed.GraphicalName);
        foreach (var number in new[] { 131, 132, 133 }) Assert.Equal(expected[$"archived-demo/mt{number:D3}.bin"], graph.Records[number]);
        Assert.All(rows.Where(row => !row.IsDefault), row => Assert.Null(row.ProfileId));
        var imported = rows.SelectMany(row => row.Records.Values).Select(raw => TablePayloadHasher.Sha256Hex(raw)).ToHashSet();
        Assert.Equal(70, expected.Count);
        Assert.All(expected.Values, raw => Assert.Contains(TablePayloadHasher.Sha256Hex(raw), imported));
    }

    [Fact]
    public void Expanded_bundle_refuses_a_blank_selected_alias_invalid_free_route_or_missing_overlay()
    {
        var (directory, _, expected) = ExpandedFixture();
        var manifestPath = Path.Combine(directory, "manifest.json"); var original = File.ReadAllText(manifestPath);
        var manifest = JsonNode.Parse(original)!;
        manifest["default_payphone_advertising"]!["MTR 1.9"] = "verified-demo/mt029.bin";
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => DemoConfigurationSeed.ReadBundle(directory));
        manifest = JsonNode.Parse(original)!;
        Replace("archived-mtr19-demo/mt029.bin", expected["verified-demo/mt029.bin"]);
        Assert.Throws<InvalidOperationException>(() => DemoConfigurationSeed.ReadBundle(directory));
        Replace("archived-mtr19-demo/mt029.bin", expected["archived-mtr19-demo/mt029.bin"]);
        var route = expected["ordinary-routing/mtr212/180-slot/mt092.bin"].ToArray(); route[3] = 0;
        Replace("ordinary-routing/mtr212/180-slot/mt092.bin", route);
        Assert.Throws<InvalidOperationException>(() => DemoConfigurationSeed.ReadBundle(directory));
        Replace("ordinary-routing/mtr212/180-slot/mt092.bin", expected["ordinary-routing/mtr212/180-slot/mt092.bin"]);
        var overlay = Path.Combine(directory, "ordinary-routing", "mtr19", "mt062-overlay.json");
        var restore = File.ReadAllText(overlay); File.WriteAllText(overlay, "{}");
        Assert.Throws<InvalidOperationException>(() => DemoConfigurationSeed.ReadBundle(directory));
        File.WriteAllText(overlay, restore);
        Assert.Equal(19, DemoConfigurationSeed.ReadBundle(directory).Length);

        void Replace(string relative, byte[] raw)
        {
            File.WriteAllBytes(Path.Combine(directory, relative), raw);
            var entry = manifest["entries"]!.AsArray().Single(row => row!["file"]!.GetValue<string>() == relative)!;
            entry["sha256"] = TablePayloadHasher.Sha256Hex(raw);
            File.WriteAllText(manifestPath, manifest.ToJsonString());
        }
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
                Assert.Equal(expected[(starter.ProfileId!, number)], raw);

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

    [PostgresIntegrationFact]
    public async Task Expanded_startup_retires_exact_initial_export_and_preserves_an_edited_initial_draft()
    {
        var (directory, legacy, _) = ExpandedFixture();
        var connection = Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)!;
        var schema = "coinline_expanded_demo_" + Guid.NewGuid().ToString("N");
        await using (var server = new NpgsqlConnection(connection))
        {
            await server.OpenAsync(); await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", server); await create.ExecuteNonQueryAsync();
        }
        var scoped = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        var options = new DbContextOptionsBuilder<HostPlatformDbContext>().UseNpgsql(scoped).Options;
        var targets = new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.LaterMultiPay180, FirmwareTableProfiles.GraphicalMultiPay180 }
            .ToDictionary(id => id, id => new FirmwareVersion { Label = "Fixture " + id, TableProfileId = id });
        Guid earlyId, laterId; byte[] edited;
        await using (var db = new HostPlatformDbContext(options))
        {
            await DatabaseBaseline.MigrateAsync(db); db.FirmwareVersions.AddRange(targets.Values); await db.SaveChangesAsync();
            await DemoConfigurationSeed.EnsureAsync(db, legacy);
        }
        await using (var db = new HostPlatformDbContext(options))
        {
            var rows = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                .Include(row => row.Versions).ThenInclude(row => row.TablePayload).ToArrayAsync();
            var early = Assert.Single(rows, row => row.Name == DemoConfigurationSeed.EarlierName);
            var later = Assert.Single(rows, row => row.Name == DemoConfigurationSeed.LaterName); earlyId = early.Id; laterId = later.Id;
            var feature = Assert.Single(later.Versions, row => row.TableDefinition!.TableNumber == 26);
            edited = TableDistributionService.GetEffectivePayload(feature).ToArray(); edited[3] ^= 1;
            new TableDistributionService(db).StageDraftPayloadReplacement(later, feature, edited);
            await db.SaveChangesAsync();
        }
        await using (var db = new HostPlatformDbContext(options)) { await DemoConfigurationSeed.EnsureAsync(db, directory); }
        await using (var db = new HostPlatformDbContext(options))
        {
            var rows = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                .Include(row => row.Versions).ThenInclude(row => row.TablePayload).ToArrayAsync();
            Assert.Equal(21, rows.Length);
            var retired = Assert.Single(rows, row => row.Id == earlyId); Assert.False(retired.IsDefault);
            Assert.Equal(DemoConfigurationSeed.EarlierName + " (initial export)", retired.Name);
            var retained = Assert.Single(rows, row => row.Id == laterId); Assert.Equal(DemoConfigurationSeed.LaterName, retained.Name);
            Assert.Equal(edited, TableDistributionService.GetEffectivePayload(Assert.Single(retained.Versions, row => row.TableDefinition!.TableNumber == 26)));
            foreach (var (name, count, profile) in new[] { (DemoConfigurationSeed.EarlierName, 13, FirmwareTableProfiles.MultiPay200),
                         (DemoConfigurationSeed.LaterName + " (complete)", 32, FirmwareTableProfiles.LaterMultiPay180),
                         (DemoConfigurationSeed.GraphicalName, 35, FirmwareTableProfiles.GraphicalMultiPay180) })
            {
                var expanded = Assert.Single(rows, row => row.Name == name); Assert.True(expanded.IsDefault);
                Assert.Equal(targets[profile].Id, expanded.TargetFirmwareVersionId); Assert.Equal(count, expanded.Versions.Count);
            }
            Assert.All(rows, row => Assert.Equal(TableSetStatus.Draft, row.Status));
            Assert.Empty(await db.TerminalTableAssignments.ToArrayAsync()); Assert.Empty(await db.DownloadBatches.ToArrayAsync());
        }
        await using (var db = new HostPlatformDbContext(options))
        {
            await DemoConfigurationSeed.EnsureAsync(db, directory); Assert.Equal(21, await db.TableSets.CountAsync());
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

    private static (string Directory, string LegacyDirectory, Dictionary<string, byte[]> Expected) ExpandedFixture()
    {
        var (directory, _) = Fixture();
        var manifestPath = Path.Combine(directory, "manifest.json"); var doc = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        var entries = doc["entries"]!.AsArray();
        var expected = new Dictionary<string, byte[]>();
        foreach (var entry in entries.ToArray())
        {
            var rel = entry!["file"]!.GetValue<string>(); if (!rel.EndsWith(".bin")) continue;
            var raw = File.ReadAllBytes(Path.Combine(directory, rel));
            if (raw[0] == 26) { raw[22] |= 1; File.WriteAllBytes(Path.Combine(directory, rel), raw); entry["sha256"] = TablePayloadHasher.Sha256Hex(raw); }
            expected.Add(rel, raw);
        }
        var legacy = Path.Combine(Path.GetTempPath(), "coinline-demo-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(legacy);
        foreach (var (rel, raw) in expected) { var file = Path.Combine(legacy, rel); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, raw); }
        File.WriteAllText(Path.Combine(legacy, "manifest.json"), doc.ToJsonString());

        var graph = GraphicalStarterWire.Create().ToDictionary(row => row.Key, row => row.Value);
        var layout = NativeTableLayouts.Find(133, graph[133], NativeTableLayouts.Later)!;
        graph[133] = layout.Apply(graph[133], new Dictionary<string, string> { ["screen-5.kind"] = "77", ["screen-6.kind"] = "77", ["screen-7.kind"] = "77" });
        Add("archived-mtr19-demo", Advertisement("OLDER DEMO")); Add("archived-demo", Advertisement("LATER DEMO"));
        foreach (var n in new[] { 55, 85, 86, 98, 99 }) Add("archived-demo", Blank(n, n == 55 ? 571 : 8001));
        foreach (var n in new[] { 131, 132, 133 }) Add("archived-demo", graph[n]);
        foreach (var n in new[] { 101, 102, 103 }) Add("archived-demo-compact", Blank(n, 403));
        Add("archived-demo-sequential", Blank(73, 1192));
        foreach (var family in new[] { "standard", "alternate" })
        {
            Add($"ordinary-routing/mtr19/{family}/60-slot", Blank(92, 1021));
            Add($"ordinary-routing/mtr19/{family}/200-slot", Screening(3401, NativeTableLayouts.MultiPay));
            foreach (var variant in new[] { "payphone", "desktop", "regional" }) Add($"advertising/mtr19/{family}-{variant}", Advertisement($"{family} {variant}"));
        }
        Add("ordinary-routing/mtr212/180-slot", Screening(3061, NativeTableLayouts.Later));
        Add("ordinary-routing/mtr212", Blank(55, 571));
        foreach (var n in new[] { 131, 132, 133 }) Add("ordinary-routing/mtr212", graph[n]);
        foreach (var variant in new[] { "payphone", "desktop" }) Add($"advertising/mtr212/{variant}", Advertisement($"later {variant}"));
        var overlays = new[] { "ordinary-routing/mtr19/mt062-overlay.json", "ordinary-routing/mtr212/mt062-overlay.json" };
        foreach (var relative in overlays)
            File.WriteAllText(Path.Combine(directory, relative), JsonSerializer.Serialize(new { message_type = 62, record_length = 353,
                segments = new[] { new { offset = 1, hex = new string('0', 36) }, new { offset = 32, hex = new string('0', 642) } } }));
        doc["default_payphone_advertising"] = JsonSerializer.SerializeToNode(new Dictionary<string, object>
        { ["MTR 1.9"] = "archived-mtr19-demo/mt029.bin", ["MTR 2.12"] = "archived-demo/mt029.bin", ["graphical"] = new[] { "archived-demo/mt131.bin", "archived-demo/mt132.bin", "archived-demo/mt133.bin" } });
        doc["ordinary_routing"] = JsonSerializer.SerializeToNode(new { earlier = "ordinary-routing/mtr19/standard/200-slot/mt092.bin", later = "ordinary-routing/mtr212/180-slot/mt092.bin", numbering_plan_overlays = overlays });
        File.WriteAllText(manifestPath, doc.ToJsonString());
        return (directory, legacy, expected);

        void Add(string group, byte[] raw)
        {
            var relative = $"{group}/mt{raw[0]:D3}.bin"; var path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, raw); expected.Add(relative, raw);
            entries.Add(JsonSerializer.SerializeToNode(new { message_type = raw[0], group, file = relative, record_length = raw.Length,
                payload_length = raw.Length - 1, sha256 = TablePayloadHasher.Sha256Hex(raw) }));
        }
        static byte[] Blank(int number, int size) { var raw = new byte[size]; raw[0] = (byte)number; return raw; }
        static byte[] Advertisement(string message)
        {
            var slots = Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false, true), 20).ToArray();
            slots[0] = new(message, 500, 0, false, false); return AdvertisingTableWire.Encode(slots);
        }
        static byte[] Screening(int size, string profile)
        {
            var raw = Blank(92, size); var layout = NativeTableLayouts.Find(92, raw, profile)!; var changes = new Dictionary<string, string>();
            var prefixes = new[] { "1800", "1888", "1877", "1866" };
            for (var i = 0; i < prefixes.Length; i++)
            {
                var key = $"rule-{i}."; changes[key + "pattern"] = prefixes[i]; changes[key + "wildcards"] = "7";
                changes[key + "classification"] = "9"; changes[key + "payment"] = "2"; changes[key + "rate"] = "255"; changes[key + "free"] = "true";
            }
            return layout.Apply(raw, changes);
        }
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
