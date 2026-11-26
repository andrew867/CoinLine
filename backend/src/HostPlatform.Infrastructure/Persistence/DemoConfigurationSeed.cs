using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Persistence;

/// <summary>Imports explicitly supplied profile-specific starter data as unpublished configurations.</summary>
public static class DemoConfigurationSeed
{
    public const string EarlierName = "Demo starter, MTR 1.9";
    public const string LaterName = "Demo starter, MTR 2.x";
    public const string GraphicalName = "Demo starter, MTR 2.x graphical";

    private static readonly int[] Earlier = [26, 27, 28, 29, 30, 32, 33, 34, 35, 50, 58, 73];
    private static readonly int[] Later = [26, 30, 32, 33, 34, 35, 50, 73, 150, 151, 152, .. Enumerable.Range(136, 14), 154, 155];

    public sealed record Starter(string Name, string? ProfileId, IReadOnlyDictionary<int, byte[]> Records, bool IsDefault = true);
    internal sealed record Bundle(Starter[] Starters, Starter[] Superseded);
    internal sealed record Manifest([property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("entries")] Entry[]? Entries,
        [property: JsonPropertyName("default_payphone_advertising")] JsonElement DefaultAdvertising = default,
        [property: JsonPropertyName("ordinary_routing")] JsonElement OrdinaryRouting = default);
    internal sealed record Entry([property: JsonPropertyName("message_type")] int Number,
        [property: JsonPropertyName("group")] string? Group, [property: JsonPropertyName("file")] string? File,
        [property: JsonPropertyName("record_length")] int RecordLength, [property: JsonPropertyName("payload_length")] int PayloadLength,
        [property: JsonPropertyName("sha256")] string? Sha256);

    public static Starter[] ReadBundle(string directory) => ReadBundleDetails(directory).Starters;

    private static Bundle ReadBundleDetails(string directory)
    {
        var root = Path.GetFullPath(directory);
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists || IsLink(rootInfo)) throw new InvalidOperationException("The demo starter directory is missing or linked.");
        var manifestPath = Path.Combine(root, "manifest.json");
        var info = new FileInfo(manifestPath);
        if (!info.Exists || info.Length is < 1 or > 1_000_000 || IsLink(info))
            throw new InvalidOperationException("The demo starter manifest is missing, oversized or linked.");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
        if (manifest?.SchemaVersion != 1 || manifest.Entries == null)
            throw new InvalidOperationException("The demo starter manifest version is not supported.");
        if (manifest.DefaultAdvertising.ValueKind != JsonValueKind.Undefined || manifest.OrdinaryRouting.ValueKind != JsonValueKind.Undefined)
            return ExpandedDemoConfigurationSeed.Read(root, manifest, Earlier, Later);
        var early = new Dictionary<int, byte[]>();
        var later = new Dictionary<int, byte[]>();
        foreach (var entry in manifest.Entries)
        {
            // Review-only metadata is intentionally not treated as a table payload.
            if (entry.File == null || !entry.File.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) continue;
            var destination = entry.Group switch
            {
                "verified-demo" or "archived-mtr19-demo" => early,
                "archived-demo" => later,
                _ => throw new InvalidOperationException("A demo starter record has an unexpected group.")
            };
            var expectedNumbers = destination == early ? Earlier : Later;
            var profile = FirmwareTableProfiles.Find(destination == early ? FirmwareTableProfiles.MultiPay200 : FirmwareTableProfiles.LaterMultiPay180)!;
            if (entry.Group == "verified-demo" && entry.Number is 50 or 73 ||
                entry.Group == "archived-mtr19-demo" && entry.Number is not (50 or 73) ||
                !expectedNumbers.Contains(entry.Number) || destination.ContainsKey(entry.Number) ||
                !profile.VerifiedLengths.TryGetValue(entry.Number, out var expectedLength) ||
                entry.RecordLength != expectedLength || entry.PayloadLength != expectedLength - 1)
                throw new InvalidOperationException($"MT{entry.Number} is duplicate or incompatible with its starter profile.");
            destination.Add(entry.Number, ReadRecord(root, entry, expectedLength));
        }
        if (!early.Keys.ToHashSet().SetEquals(Earlier) || !later.Keys.ToHashSet().SetEquals(Later))
            throw new InvalidOperationException("The demo starter bundle is incomplete.");
        return new([new(EarlierName, FirmwareTableProfiles.MultiPay200, early), new(LaterName, FirmwareTableProfiles.LaterMultiPay180, later)], []);
    }

    internal static byte[] ReadRecord(string root, Entry entry, int expectedLength)
    {
        var expectedFile = $"{entry.Group}/mt{entry.Number:D3}.bin";
        if (entry.File == null || !string.Equals(entry.File.Replace('\\', '/'), expectedFile, StringComparison.Ordinal) ||
            entry.RecordLength != expectedLength || entry.PayloadLength != expectedLength - 1)
            throw new InvalidOperationException($"MT{entry.Number} has an unexpected starter file path or length.");
        var file = CheckedFile(root, expectedFile);
        if (file.Length != expectedLength) throw new InvalidOperationException($"MT{entry.Number} has the wrong length.");
        var raw = File.ReadAllBytes(file.FullName);
        if (raw.Length != expectedLength || raw[0] != entry.Number ||
            !string.Equals(TablePayloadHasher.Sha256Hex(raw), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"MT{entry.Number} does not match the starter manifest digest and message type.");
        return raw;
    }

    internal static FileInfo CheckedFile(string root, string relative)
    {
        var pieces = relative.Split('/');
        if (pieces.Any(p => string.IsNullOrEmpty(p) || p is "." or ".." || p.Contains('\\') || p.Contains(':')))
            throw new InvalidOperationException("Invalid starter file path.");
        var parent = root;
        foreach (var piece in pieces[..^1])
        {
            parent = Path.Combine(parent, piece);
            var group = new DirectoryInfo(parent);
            if (!group.Exists || IsLink(group)) throw new InvalidOperationException("A starter directory is missing or linked.");
        }
        var file = new FileInfo(Path.Combine(parent, pieces[^1]));
        if (!file.Exists || IsLink(file)) throw new InvalidOperationException("A starter file is missing or linked.");
        return file;
    }

    /// <summary>Never replaces an existing starter, publishes it, or assigns it to any phone.</summary>
    public static async Task EnsureAsync(HostPlatformDbContext db, string directory, CancellationToken ct = default)
    {
        var bundle = ReadBundleDetails(directory);
        var starters = bundle.Starters;
        var names = starters.Select(row => row.Name).Concat(bundle.Superseded.SelectMany(row => new[] { PreviousName(row), ReplacementName(row) })).ToArray();
        var existing = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
            .Include(row => row.Versions).ThenInclude(row => row.TablePayload).Where(row => names.Contains(row.Name)).ToListAsync(ct);
        var definitions = await db.TableDefinitions.ToListAsync(ct);
        var reference = RecordReferenceCatalogue.Build([]).ToDictionary(row => row.Number);
        foreach (var starter in starters)
        {
            var name = starter.Name;
            var collisions = existing.Where(row => row.Name == name).ToArray();
            if (collisions.Length > 0)
            {
                var old = bundle.Superseded.SingleOrDefault(row => row.Name == name);
                // Never overwrite operator edits. Only an exact, unpublished prior
                // default is retired as a non-default reference.
                if (old == null || collisions.Length != 1 || !HasShape(collisions[0], old)) continue;
                var prior = collisions[0];
                if (HasExactRecords(prior, starter)) continue;
                if (prior.Status == TableSetStatus.Draft && prior.IsDefault && HasExactRecords(prior, old)
                    && !existing.Any(row => row.Name == PreviousName(old)))
                {
                    prior.Name = PreviousName(old); prior.IsDefault = false; prior.Version++;
                }
                else name = ReplacementName(old);
                if (existing.Any(row => row.Name == name)) continue;
            }
            var targetIds = starter.ProfileId == null ? [] : await db.FirmwareVersions.AsNoTracking().Where(row => row.TableProfileId == starter.ProfileId)
                .Select(row => row.Id).Take(2).ToArrayAsync(ct);
            var set = new TableSet { Name = name, Status = TableSetStatus.Draft, IsDefault = starter.IsDefault,
                TargetFirmwareVersionId = targetIds.Length == 1 ? targetIds[0] : null };
            foreach (var (number, raw) in starter.Records.OrderBy(row => row.Key))
            {
                var definitionName = reference[number].Name;
                var definition = definitions.FirstOrDefault(row => row.TableNumber == number && string.Equals(row.Name, definitionName, StringComparison.OrdinalIgnoreCase));
                if (definition == null)
                {
                    definition = new TableDefinition { Name = definitionName, TableNumber = number, Description = "Profile-specific configuration starter record." };
                    definitions.Add(definition); db.TableDefinitions.Add(definition);
                }
                var hash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
                var payload = new TablePayload { RawContent = raw, Sha256Hex = hash, LengthBytes = raw.Length };
                set.Versions.Add(new TableVersion { TableDefinition = definition, TableRevision = 1, SortOrder = number,
                    TablePayload = payload, Checksum = SHA256.HashData(raw), PayloadSha256Hex = hash, ValidationPassed = true });
            }
            db.TableSets.Add(set); existing.Add(set);
        }
        await db.SaveChangesAsync(ct);
    }

    private static string PreviousName(Starter starter) => starter.Name + (starter.Name == GraphicalName
        ? " (text-enabled reference)" : " (initial export)");

    private static string ReplacementName(Starter starter) => starter.Name + (starter.Name == GraphicalName
        ? " (graphical idle default)" : " (complete)");

    private static bool HasShape(TableSet set, Starter starter) => set.Versions.Count == starter.Records.Count &&
        set.Versions.Select(row => row.TableDefinition?.TableNumber ?? 0).ToHashSet().SetEquals(starter.Records.Keys);

    private static bool HasExactRecords(TableSet set, Starter starter) => HasShape(set, starter) &&
        set.Versions.All(row => starter.Records[row.TableDefinition!.TableNumber].AsSpan().SequenceEqual(TableDistributionService.GetEffectivePayload(row)));

    private static bool IsLink(FileSystemInfo item) => (item.Attributes & FileAttributes.ReparsePoint) != 0;
}
