using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

/// <summary>Validated decimal terminal-ID mapping and an immutable MT21 identity substitution.</summary>
public static class TerminalIdCutover
{
    public static bool IsDecimalId(string? value) =>
        value is { Length: 10 } && value.All(char.IsAsciiDigit);

    public static bool IsMapping(string? source, string? target) =>
        source is { Length: 6 or 10 } && target != null && target.Length == source.Length &&
        source.All(char.IsAsciiDigit) && target.All(char.IsAsciiDigit) && source != target;

    public static string? Map(string wireId, string source, string target) =>
        IsMapping(source, target) && IsDecimalId(wireId) && wireId.StartsWith(source, StringComparison.Ordinal)
            ? target + wireId[source.Length..] : null;

    public static byte[] RewriteAccessRecord(TableVersion version, FirmwareTableProfile? profile,
        string originalId, string replacementId)
    {
        if (!IsDecimalId(originalId) || !IsDecimalId(replacementId) || originalId == replacementId)
            throw new InvalidOperationException("Use distinct ten-digit decimal terminal IDs.");
        if (profile is not { ObservedLengthsOnly: false } ||
            !profile.VerifiedLengths.TryGetValue(21, out var length) || length is not (18 or 48))
            throw new InvalidOperationException("A verified terminal access layout is required for this firmware.");

        var bytes = TableDistributionService.GetEffectivePayload(version);
        if (version.TableDefinition?.TableNumber != 21 || !version.ValidationPassed ||
            bytes.Length != length || bytes[0] != 21)
            throw new InvalidOperationException("The assigned MT21 is not a validated record for this firmware.");
        if (version.DependsOnTableDefinitionId != null)
            throw new InvalidOperationException("The access record must be deliverable without another record.");
        if (!bytes.AsSpan(1, 5).SequenceEqual(Convert.FromHexString(originalId)))
            throw new InvalidOperationException("The assigned MT21 does not contain the phone's current wire ID.");

        var revised = bytes.ToArray();
        Convert.FromHexString(replacementId).CopyTo(revised, 1);
        return revised;
    }
}
