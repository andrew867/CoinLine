using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record PortableConfigurationRecord(int Number, string PayloadBase64, string Sha256, int SortOrder, int? DependsOnNumber);
public sealed record PortableConfigurationPackage(string Schema, string ProfileId, PortableConfigurationRecord[] Records, int[] OmittedNumbers);
public sealed record PortableConfigurationReview(int Number, string Action, string Sha256, int Length, bool RetainedLocalFields);
public sealed record PortableConfigurationPreparation(TableSet Draft, PortableConfigurationReview[] Records, int[] RetainedNumbers,
    int[] MissingOmittedNumbers, TableFirmwareFinding[] Findings, string PreviewToken);

/// <summary>Cross-installation configuration templates; never an identity, credential or fleet backup.</summary>
public static class PortableTerminalConfiguration
{
    public const string Schema = "coinline-terminal-configuration-v1";
    public const int MaxPackageBytes = 1_000_000;
    private const int MaxRecords = 64;
    private static readonly HashSet<int> Shareable =
    [
        26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 49, 50, 55, 58, 62, 73, 85, 86, 92,
        101, 102, 103, 104, 105, 106, 107, 131, 132, 133,
        .. Enumerable.Range(136, 17), 154, 155
    ];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PortableConfigurationPackage Export(TableSet source)
    {
        var profile = Profile(source);
        var records = ByNumber(source);
        var omitted = records.Keys.Where(number => !Shareable.Contains(number) || !profile.VerifiedLengths.ContainsKey(number)).Order().ToArray();
        var output = new List<PortableConfigurationRecord>();
        var byDefinition = records.ToDictionary(pair => pair.Value.TableDefinitionId, pair => pair.Key);
        foreach (var (number, version) in records.OrderBy(pair => pair.Key))
        {
            if (omitted.Contains(number)) continue;
            var raw = CheckedRecord(profile, number, version).ToArray();
            foreach (var (offset, length) in LocalRanges(profile, number, raw)) raw.AsSpan(offset, length).Clear();
            int? dependsOn = null;
            if (version.DependsOnTableDefinitionId is { } dependency)
            {
                if (!byDefinition.TryGetValue(dependency, out var dependencyNumber)) throw new InvalidOperationException("A source dependency is missing from the set.");
                dependsOn = dependencyNumber;
            }
            output.Add(new(number, Convert.ToBase64String(raw), TablePayloadHasher.Sha256Hex(raw), version.SortOrder, dependsOn));
        }
        if (output.Count == 0) throw new InvalidOperationException("This set has no portable records for its registered profile.");
        // Apply the same structural checks used by imports before returning any package.
        var package = new PortableConfigurationPackage(Schema, profile.Id, output.ToArray(), omitted);
        _ = Decode(package, profile);
        return package;
    }

    public static PortableConfigurationPreparation Prepare(TableSet baseline, PortableConfigurationPackage package, string? name,
        IReadOnlyCollection<TableDefinition> definitions)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a draft name of up to 120 characters.");
        var profile = Profile(baseline);
        var decoded = Decode(package, profile);
        var originals = ByNumber(baseline);
        var draft = new TableSet { Name = name.Trim(), CustomerId = baseline.CustomerId,
            TargetFirmwareVersionId = baseline.TargetFirmwareVersionId, TargetFirmwareVersion = baseline.TargetFirmwareVersion };
        var copies = originals.ToDictionary(pair => pair.Key, pair =>
        {
            var original = pair.Value;
            var copy = TableDistributionService.CopyVersion(original);
            copy.TableDefinition = original.TableDefinition;
            copy.TablePayload = original.TablePayload;
            draft.Versions.Add(copy);
            return copy;
        });
        // Keep existing local definitions, even if other sets register the same
        // record number. Require an unambiguous local definition for new rows.
        foreach (var number in decoded.Keys.Where(number => !copies.ContainsKey(number)))
        {
            var matching = definitions.Where(definition => definition.TableNumber == number).ToArray();
            if (matching.Length != 1) throw new InvalidOperationException($"Register exactly one local definition for MT{number} before adding it.");
            var definition = matching[0];
            var copy = new TableVersion { TableDefinition = definition, TableDefinitionId = definition.Id };
            copies.Add(number, copy);
            draft.Versions.Add(copy);
        }
        var review = new List<PortableConfigurationReview>();
        foreach (var (number, input) in decoded.OrderBy(pair => pair.Key))
        {
            var imported = input.Bytes.ToArray();
            var localRanges = LocalRanges(profile, number, imported);
            if (localRanges.Length != 0)
            {
                if (!originals.TryGetValue(number, out var original))
                    throw new InvalidOperationException($"Prepare MT{number} in the local base set before importing its portable fields.");
                var local = CheckedRecord(profile, number, original);
                foreach (var (offset, length) in localRanges)
                    local.AsSpan(offset, length).CopyTo(imported.AsSpan(offset, length));
            }
            var row = copies[number];
            var prior = originals.GetValueOrDefault(number);
            var same = prior != null && TableDistributionService.GetEffectivePayload(prior).AsSpan().SequenceEqual(imported);
            var dependency = input.Record.DependsOnNumber;
            if (dependency is { } required && !copies.ContainsKey(required))
                throw new InvalidOperationException($"MT{number} requires MT{required}, which is absent from the local base and package.");
            row.DependsOnTableDefinitionId = dependency is { } dep ? copies[dep].TableDefinitionId : null;
            row.SortOrder = input.Record.SortOrder;
            if (!same)
            {
                var hash = TablePayloadHasher.Sha256Hex(imported);
                row.TablePayload = new TablePayload { RawContent = imported, LengthBytes = imported.Length, Sha256Hex = hash };
                row.TablePayloadId = null;
                row.EmbeddedPayload = null;
                row.PayloadSha256Hex = hash;
                row.Checksum = SHA256.HashData(imported);
                row.TableRevision = prior == null ? 1 : checked(prior.TableRevision + 1);
                (row.ValidationPassed, row.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(row.TableDefinition!, imported);
            }
            // Host-specific assignment labels or source customer references are
            // not portable. Keep local labels only on byte-identical records.
            if (!same)
            {
                row.ConfigurationId = null;
                row.ScreeningDescriptionsJson = null;
                row.CardMasterLinksJson = null;
            }
            if (number == 92) row.ScreeningLayoutProfile = profile.LayoutFor(number);
            // Never expose a hash of the restored credential-bearing payload in
            // the preview. Report the package hash, not the local secret.
            review.Add(new(number, prior == null ? "add" : same ? "unchanged" : "replace", input.Record.Sha256, imported.Length, localRanges.Length != 0));
        }
        _ = TableDistributionService.OrderVersionsForDownload(draft.Versions.ToArray());
        var retained = originals.Keys.Where(number => !decoded.ContainsKey(number)).Order().ToArray();
        var missing = package.OmittedNumbers.Where(number => !copies.ContainsKey(number)).Order().ToArray();
        // Source revision and local record revisions bind preview to the chosen
        // base without hashing or returning local credential values.
        var token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            baseline.Id, baseline.Version, baseline.TargetFirmwareVersionId, draft.Name, package,
            local = originals.OrderBy(pair => pair.Key).Select(pair => new { pair.Key, pair.Value.Id, pair.Value.Version, pair.Value.TableRevision })
        }, Json))).ToLowerInvariant();
        return new(draft, review.ToArray(), retained, missing, TableSetFirmwareCompatibility.Review(draft).ToArray(), token);
    }

    private static Dictionary<int, (PortableConfigurationRecord Record, byte[] Bytes)> Decode(PortableConfigurationPackage package, FirmwareTableProfile profile)
    {
        if (package.Schema != Schema || package.ProfileId != profile.Id || package.Records == null || package.OmittedNumbers == null ||
            package.Records.Length is < 1 or > MaxRecords || package.OmittedNumbers.Length > MaxRecords)
            throw new ArgumentException("Choose a version 1 package matching the local verified terminal profile.");
        if (package.OmittedNumbers.Any(number => number is < 1 or > 255) || package.OmittedNumbers.Distinct().Count() != package.OmittedNumbers.Length)
            throw new ArgumentException("Package omission list contains invalid or duplicate record numbers.");
        var decoded = new Dictionary<int, (PortableConfigurationRecord, byte[])>();
        foreach (var record in package.Records)
        {
            if (record == null || !Shareable.Contains(record.Number) || !profile.VerifiedLengths.TryGetValue(record.Number, out var length) ||
                record.SortOrder is < 0 or > 1_000_000 || record.DependsOnNumber is < 1 or > 255 || record.DependsOnNumber == record.Number ||
                package.OmittedNumbers.Contains(record.Number) || decoded.ContainsKey(record.Number))
                throw new ArgumentException("Package contains an invalid, duplicate, incompatible or nonportable record.");
            if (record.PayloadBase64 == null || record.PayloadBase64.Length != 4 * ((length + 2) / 3) ||
                record.Sha256 == null || record.Sha256.Length != 64)
                throw new ArgumentException($"MT{record.Number} has an invalid payload length or digest.");
            byte[] raw;
            try { raw = Convert.FromBase64String(record.PayloadBase64); }
            catch (FormatException) { throw new ArgumentException($"MT{record.Number} has an invalid base64 payload."); }
            if (raw.Length != length || raw[0] != record.Number ||
                !string.Equals(TablePayloadHasher.Sha256Hex(raw), record.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"MT{record.Number} does not match its declared profile, type or digest.");
            foreach (var (offset, count) in LocalRanges(profile, record.Number, raw))
                if (raw.AsSpan(offset, count).IndexOfAnyExcept((byte)0) >= 0)
                    throw new ArgumentException($"MT{record.Number} contains nonportable local fields. Export a reviewed template instead.");
            decoded.Add(record.Number, (record, raw));
        }
        return decoded;
    }

    private static FirmwareTableProfile Profile(TableSet set)
    {
        var profile = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        if (profile == null || profile.ObservedLengthsOnly)
            throw new InvalidOperationException("Choose a set with a verified native terminal profile.");
        return profile;
    }

    private static Dictionary<int, TableVersion> ByNumber(TableSet set)
    {
        if (set.Versions.Any(row => row.TableDefinition == null) || set.Versions.GroupBy(row => row.TableDefinition!.TableNumber).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Resolve missing or duplicate table definitions before sharing or importing.");
        return set.Versions.ToDictionary(row => row.TableDefinition!.TableNumber);
    }

    private static byte[] CheckedRecord(FirmwareTableProfile profile, int number, TableVersion version)
    {
        var raw = TableDistributionService.GetEffectivePayload(version);
        if (!version.ValidationPassed || !profile.VerifiedLengths.TryGetValue(number, out var length) || raw.Length != length || raw[0] != number ||
            (!string.IsNullOrEmpty(version.PayloadSha256Hex) && !string.Equals(version.PayloadSha256Hex, TablePayloadHasher.Sha256Hex(raw), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Review the invalid or incompatible MT{number} record before sharing or importing.");
        return raw;
    }

    private static (int Offset, int Length)[] LocalRanges(FirmwareTableProfile profile, int number, byte[] raw)
    {
        var layout = NativeTableLayouts.Find(number, raw, profile.LayoutFor(number));
        var credentials = layout?.Sections.SelectMany(section => section.Fields).Where(field => field.IsCredential)
            .Select(field => (field.Offset, field.Width)).Distinct().OrderBy(field => field.Offset).ToArray() ?? [];
        if (number == 31 && !credentials.SequenceEqual(new[] { (1, 4), (5, 5) }) ||
            number == 62 && !credentials.SequenceEqual(new[] { (21, 5) }) ||
            number is not (31 or 62) && credentials.Length != 0)
            throw new InvalidOperationException($"MT{number} has an unreviewed protected layout.");
        // Local calendar dates and one-time clock adjustments are state, not
        // reusable call-in scheduling configuration.
        return number == 35 ? [(1, 3), (13, 8)] : credentials;
    }
}
