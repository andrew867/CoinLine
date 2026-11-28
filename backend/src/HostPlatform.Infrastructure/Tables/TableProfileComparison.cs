using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record TableProfileRecordComparison(int TableNumber, int? ActualLength, int? ExpectedLength,
    string Status, bool LayoutReviewRequired, string Message);

public sealed record TableProfileComparisonResult(string ProfileId, string Label, bool CompleteCatalogue,
    bool ObservedLengthsOnly, IReadOnlyList<TableProfileRecordComparison> Records)
{
    public int MatchingLengthCount => Records.Count(record => record.Status == "match");
    public int UnknownCount => Records.Count(record => record.Status == "unknown");
    public int ConflictCount => Records.Count(record => record.Status == "conflict");
    public int LayoutReviewCount => Records.Count(record => record.LayoutReviewRequired);
}

/// <summary>Compares existing record shapes, without identifying the terminal or changing a set.</summary>
public static class TableProfileComparison
{
    public static IReadOnlyList<TableProfileComparisonResult> Compare(TableSet set)
    {
        var groups = set.Versions.GroupBy(record => record.TableDefinition?.TableNumber ?? 0)
            .OrderBy(group => group.Key).ToArray();
        if (groups.Length == 0)
            return [];

        return FirmwareTableProfiles.Catalog.Select(profile => new TableProfileComparisonResult(
            profile.Id, profile.Label, profile.CompleteCatalogue, profile.ObservedLengthsOnly,
            groups.Select(group => CompareRecord(profile, group)).ToArray()))
            .OrderBy(result => result.ConflictCount)
            .ThenByDescending(result => result.MatchingLengthCount)
            .ThenBy(result => result.Label, StringComparer.Ordinal)
            .ToArray();
    }

    private static TableProfileRecordComparison CompareRecord(FirmwareTableProfile profile,
        IGrouping<int, TableVersion> group)
    {
        var number = group.Key;
        int? expected = profile.VerifiedLengths.TryGetValue(number, out var length) ? length : null;
        if (group.Count() != 1)
            return new(number, null, expected, "conflict", false, "Multiple records share this message type.");

        var version = group.Single();
        var data = TableDistributionService.GetEffectivePayload(version);
        if (number is < 1 or > 255 || data.Length <= 2 || data[0] != number)
            return new(number, data.Length, expected, "conflict", false,
                "The record is incomplete or its message type does not match its definition.");
        if (!version.ValidationPassed)
            return new(number, data.Length, expected, "conflict", false,
                "The saved record failed validation.");
        if (TableSetFirmwareCompatibility.RecordIssue(profile, number, data.Length) is { } issue)
            return new(number, data.Length, expected, "conflict", false, issue);
        if (expected == null)
            return new(number, data.Length, null, "unknown", false,
                "This incomplete catalogue does not establish the record length.");

        var confirmed = version.ScreeningLayoutProfile;
        var target = profile.LayoutFor(number);
        var layouts = NativeTableLayouts.Candidates(number, data);
        if (confirmed != null && layouts.Count > 0 && layouts.All(layout => layout.Profile != confirmed))
            return new(number, data.Length, expected, "conflict", false,
                "The saved field layout is not available at this record length.");
        if (confirmed != null && target != null && confirmed != target)
            return new(number, data.Length, expected, "conflict", false,
                "The saved record's confirmed field layout differs from this profile.");

        var review = profile.ObservedLengthsOnly || (confirmed == null && layouts.Count > 1);
        return new(number, data.Length, expected, "match", review, review
            ? "The known length matches; confirm the field layout independently."
            : "The known record length matches.");
    }
}
