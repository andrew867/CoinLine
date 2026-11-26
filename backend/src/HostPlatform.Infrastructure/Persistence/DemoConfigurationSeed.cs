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

    private static readonly int[] Earlier = [26, 27, 28, 29, 30, 32, 33, 34, 35, 50, 58, 73];
    private static readonly int[] Later = [26, 30, 32, 33, 34, 35, 50, 73, 150, 151, 152, .. Enumerable.Range(136, 14), 154, 155];

    public sealed record Starter(string Name, string ProfileId, IReadOnlyDictionary<int, byte[]> Records);
    private sealed record Manifest([property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("entries")] Entry[]? Entries);
    private sealed record Entry([property: JsonPropertyName("message_type")] int Number,
        [property: JsonPropertyName("group")] string? Group, [property: JsonPropertyName("file")] string? File,
        [property: JsonPropertyName("record_length")] int RecordLength, [property: JsonPropertyName("payload_length")] int PayloadLength,
        [property: JsonPropertyName("sha256")] string? Sha256);

    public static Starter[] ReadBundle(string directory)
    {
        var root = Path.GetFullPath(directory);
        var manifestPath = Path.Combine(root, "manifest.json");
        var info = new FileInfo(manifestPath);
        if (!info.Exists || info.Length is < 1 or > 1_000_000 || IsLink(info))
            throw new InvalidOperationException("The demo starter manifest is missing, oversized or linked.");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
        if (manifest?.SchemaVersion != 1 || manifest.Entries == null)
            throw new InvalidOperationException("The demo starter manifest version is not supported.");
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
            var expectedFile = $"{entry.Group}/mt{entry.Number:D3}.bin";
            if (!string.Equals(entry.File.Replace('\\', '/'), expectedFile, StringComparison.Ordinal))
                throw new InvalidOperationException($"MT{entry.Number} has an unexpected starter file path.");
            var group = new DirectoryInfo(Path.Combine(root, entry.Group!));
            var file = new FileInfo(Path.Combine(group.FullName, $"mt{entry.Number:D3}.bin"));
            if (!group.Exists || IsLink(group) || !file.Exists || IsLink(file) || file.Length != expectedLength)
                throw new InvalidOperationException($"MT{entry.Number} is missing, linked or has the wrong length.");
            var raw = File.ReadAllBytes(file.FullName);
            if (raw.Length != expectedLength || raw[0] != entry.Number ||
                !string.Equals(TablePayloadHasher.Sha256Hex(raw), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"MT{entry.Number} does not match the starter manifest digest and message type.");
            destination.Add(entry.Number, raw);
        }
        if (!early.Keys.ToHashSet().SetEquals(Earlier) || !later.Keys.ToHashSet().SetEquals(Later))
            throw new InvalidOperationException("The demo starter bundle is incomplete.");
        return [new(EarlierName, FirmwareTableProfiles.MultiPay200, early), new(LaterName, FirmwareTableProfiles.LaterMultiPay180, later)];
    }

    /// <summary>Never replaces an existing starter, publishes it, or assigns it to any phone.</summary>
    public static async Task EnsureAsync(HostPlatformDbContext db, string directory, CancellationToken ct = default)
    {
        var starters = ReadBundle(directory);
        var names = starters.Select(row => row.Name).ToArray();
        var existing = await db.TableSets.AsNoTracking().Where(row => names.Contains(row.Name)).Select(row => row.Name).ToListAsync(ct);
        var definitions = await db.TableDefinitions.ToListAsync(ct);
        var reference = RecordReferenceCatalogue.Build([]).ToDictionary(row => row.Number);
        foreach (var starter in starters.Where(row => !existing.Contains(row.Name, StringComparer.Ordinal)))
        {
            var targetIds = await db.FirmwareVersions.AsNoTracking().Where(row => row.TableProfileId == starter.ProfileId)
                .Select(row => row.Id).Take(2).ToArrayAsync(ct);
            var set = new TableSet { Name = starter.Name, Status = TableSetStatus.Draft, IsDefault = true,
                TargetFirmwareVersionId = targetIds.Length == 1 ? targetIds[0] : null };
            foreach (var (number, raw) in starter.Records.OrderBy(row => row.Key))
            {
                var name = reference[number].Name;
                var definition = definitions.FirstOrDefault(row => row.TableNumber == number && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase));
                if (definition == null)
                {
                    definition = new TableDefinition { Name = name, TableNumber = number, Description = "Profile-specific configuration starter record." };
                    definitions.Add(definition); db.TableDefinitions.Add(definition);
                }
                var hash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
                var payload = new TablePayload { RawContent = raw, Sha256Hex = hash, LengthBytes = raw.Length };
                set.Versions.Add(new TableVersion { TableDefinition = definition, TableRevision = 1, SortOrder = number,
                    TablePayload = payload, Checksum = SHA256.HashData(raw), PayloadSha256Hex = hash, ValidationPassed = true });
            }
            db.TableSets.Add(set);
        }
        await db.SaveChangesAsync(ct);
    }

    private static bool IsLink(FileSystemInfo item) => (item.Attributes & FileAttributes.ReparsePoint) != 0;
}
