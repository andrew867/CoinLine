using System.Globalization;

namespace HostPlatform.Protocols.Tables;

public sealed record SetupTransitionField(int TableNumber, string Key, string Label, string Action, bool Protected,
    string? SourceValue, string? TargetBefore, string? TargetAfter);
public sealed record SetupTransitionReference(int SourceSlot, string[] Fields, int[] Options, int? TargetSlot);
public sealed record SetupTransitionFinding(bool Blocking, int TableNumber, string Message);
public sealed record SetupTransitionProjection(byte[] Features, byte[] Installation, SetupTransitionField[] Fields,
    SetupTransitionReference[] References, int[] ChangedRecords, SetupTransitionFinding[] Findings);

/// <summary>Project only identified common MT26 and MT31 fields, retaining the target's distinct meanings and spare bytes.</summary>
public static class TerminalSetupTransitionWire
{
    private static readonly string[] ScreeningKeys = ["intraOperatorReference", "operatorReference"];

    public static SetupTransitionProjection Project(byte[] sourceFeatures, byte[] targetFeatures, byte[] sourceInstallation,
        byte[] targetInstallation, byte[] sourceScreening, byte[] targetScreening, byte[]? sourceTariffs = null, byte[]? targetTariffs = null,
        IReadOnlyDictionary<int, int>? referenceChoices = null, bool carryCredentials = true, bool retainPresentation = false,
        byte[]? sourceCarriers = null, byte[]? targetCarriers = null)
    {
        var oldFeatures = Layout(26, sourceFeatures, NativeTableLayouts.MultiPay);
        var newFeatures = Layout(26, targetFeatures, NativeTableLayouts.Later);
        var oldInstallation = Layout(31, sourceInstallation, NativeTableLayouts.MultiPay);
        var newInstallation = Layout(31, targetInstallation, NativeTableLayouts.Later);
        var oldScreen = Layout(92, sourceScreening, NativeTableLayouts.MultiPay);
        var newScreen = Layout(92, targetScreening, NativeTableLayouts.Later);
        if (oldScreen.Length != 3401 || newScreen.Length != 3061)
            throw new InvalidOperationException("Choose the identified 200-entry source and 180-entry target screening layouts.");
        var features = targetFeatures.ToArray(); var installation = targetInstallation.ToArray();
        var fields = new List<SetupTransitionField>(); var references = new List<SetupTransitionReference>(); var findings = new List<SetupTransitionFinding>();
        var sourceByKey = oldFeatures.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        var sourcePointers = ScreeningKeys.ToDictionary(key => key, key => (int)sourceFeatures[sourceByKey[key].Offset]);

        foreach (var group in sourcePointers.Where(pair => pair.Value != 0).GroupBy(pair => pair.Value).OrderBy(group => group.Key))
        {
            var slot = group.Key;
            var options = slot <= 200 && CallScreeningWire.CanCopyEntry(sourceScreening, oldScreen, slot)
                ? Enumerable.Range(1, 180).Where(position => CallScreeningWire.CanCopyEntry(targetScreening, newScreen, position) &&
                    MatchingScreening(ScreenRow(sourceScreening, slot), ScreenRow(targetScreening, position), sourceTariffs, targetTariffs, sourceCarriers, targetCarriers)).ToArray() : [];
            int? selected = null;
            if (referenceChoices?.TryGetValue(slot, out var chosen) == true)
            {
                if (options.Contains(chosen)) selected = chosen;
                else Block(26, $"The selected target screening position does not match source operator reference {slot}.");
            }
            else if (options.Length == 1) selected = options[0];
            if (selected == null)
                Block(26, options.Length == 0 ? $"Source operator screening reference {slot} has no matching target rule and rate/carrier meaning. Prepare it first."
                    : $"Choose the matching target screening position for source operator reference {slot}.");
            references.Add(new(slot, group.Select(pair => sourceByKey[pair.Key].Label).ToArray(), options, selected));
        }
        if (referenceChoices?.Keys.Any(slot => !references.Any(reference => reference.SourceSlot == slot)) == true)
            Block(26, "A submitted mapping does not belong to a configured source operator reference.");

        Carry(oldFeatures, newFeatures, sourceFeatures, targetFeatures, features);
        Carry(oldInstallation, newInstallation, sourceInstallation, targetInstallation, installation);
        findings.Add(new(false, 26, "Earlier incoming-call rate, operator/toll conversion and default language-slot settings have no identified direct later mapping. Review those separately alongside the candidate's language order and routing controls."));
        if (retainPresentation) findings.Add(new(false, 26, "The prepared target display and advertising controls remain as supplied. Review their behavior on the target display hardware."));
        findings.Add(new(false, 31, "Later packet-data predial, analog-prefix, cashbox-query and Craft reporting controls stay as supplied. Earlier spare bits are never interpreted as those later fields."));
        findings.Add(new(false, 26, "Terminal payment mode, display controls, card-alert timing and other shared values are transferred as configuration only. Actual hardware and feature behavior require qualification."));
        var changed = new List<int>(); if (!features.SequenceEqual(targetFeatures)) changed.Add(26); if (!installation.SequenceEqual(targetInstallation)) changed.Add(31);
        return new(features, installation, fields.ToArray(), references.ToArray(), changed.ToArray(), findings.Distinct().ToArray());

        void Carry(NativeTableLayout sourceLayout, NativeTableLayout targetLayout, byte[] source, byte[] before, byte[] output)
        {
            var targetFields = targetLayout.Sections.SelectMany(section => section.Fields).Where(field => !field.IsSpare).ToDictionary(field => field.Key);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in sourceLayout.Sections)
            foreach (var old in section.Fields.Where(field => !field.IsSpare))
            {
                var secret = Protected(old);
                if (!targetFields.TryGetValue(old.Key, out var next) || !SameShape(old, next))
                {
                    fields.Add(new(sourceLayout.Number, old.Key, old.Label, "source-only", secret, Display(old, source), null, null));
                    continue;
                }
                seen.Add(old.Key);
                var action = "unchanged";
                if ((sourceLayout.Number == 26 && retainPresentation && section.Key == "display") || (old.IsCredential && !carryCredentials))
                    action = "retained-target";
                else if (sourceLayout.Number == 26 && ScreeningKeys.Contains(old.Key))
                {
                    var slot = sourcePointers[old.Key];
                    var selected = slot == 0 ? 0 : references.Single(reference => reference.SourceSlot == slot).TargetSlot;
                    if (selected == null) action = "unresolved";
                    else output[next.Offset] = (byte)selected.Value;
                }
                else if (old.IsCredential && !ValidCredential(old, source))
                {
                    if (source.AsSpan(old.Offset, old.Width).SequenceEqual(before.AsSpan(next.Offset, next.Width)))
                        findings.Add(new(false, sourceLayout.Number, $"The identical stored {old.Label.ToLowerInvariant()} bytes are retained but their format is not established. Review the configured value."));
                    else { Block(sourceLayout.Number, $"The stored {old.Label.ToLowerInvariant()} format is not recognized. Review the source or explicitly retain the prepared target credentials."); action = "unresolved"; }
                }
                else if (!old.IsCredential && !ValidShared(old, next, source))
                {
                    if (ValueBytesEqual(old, source, before))
                        findings.Add(new(false, sourceLayout.Number, $"An identical {old.Label.ToLowerInvariant()} value is not recognized by the typed editor and is left unchanged."));
                    else { Block(sourceLayout.Number, $"The source {old.Label.ToLowerInvariant()} value has no recognized common representation. Review the source first."); action = "unresolved"; }
                }
                else Copy(old, source, output);
                if (action == "unchanged" && !ValueBytesEqual(next, before, output)) action = "changed";
                fields.Add(new(sourceLayout.Number, old.Key, old.Label, action, secret, Display(old, source), Display(next, before), Display(next, output)));
            }
            foreach (var next in targetFields.Values.Where(field => !seen.Contains(field.Key)))
                fields.Add(new(targetLayout.Number, next.Key, next.Label, "target-only", Protected(next), null, Display(next, before), Display(next, output)));
        }
        void Block(int number, string message) => findings.Add(new(true, number, message));
    }

    private static NativeTableLayout Layout(int number, byte[] raw, string profile) => NativeTableLayouts.Find(number, raw, profile)
        ?? throw new InvalidOperationException($"MT{number} is missing or does not match the identified {profile} layout.");
    private static bool SameShape(NativeTableField old, NativeTableField next) => old.Offset == next.Offset && old.Width == next.Width &&
        old.Encoding == next.Encoding && old.Mask == next.Mask && old.IsCredential == next.IsCredential;
    private static bool Protected(NativeTableField field) => field.IsCredential || field.Encoding is TableFieldEncoding.Dial or TableFieldEncoding.KeypadDial;
    private static bool Masked(NativeTableField field) => field.Encoding is TableFieldEncoding.Flag or TableFieldEncoding.Bits;
    private static bool ValueBytesEqual(NativeTableField field, byte[] first, byte[] second) => Masked(field)
        ? (first[field.Offset] & field.Mask) == (second[field.Offset] & field.Mask)
        : first.AsSpan(field.Offset, field.Width).SequenceEqual(second.AsSpan(field.Offset, field.Width));
    private static void Copy(NativeTableField field, byte[] source, byte[] target)
    {
        if (Masked(field)) target[field.Offset] = (byte)((target[field.Offset] & ~field.Mask) | (source[field.Offset] & field.Mask));
        else source.AsSpan(field.Offset, field.Width).CopyTo(target.AsSpan(field.Offset, field.Width));
    }
    private static bool ValidShared(NativeTableField old, NativeTableField next, byte[] source)
    {
        var value = old.Read(source); if (value == null) return false;
        if (old.Encoding is TableFieldEncoding.Flag or TableFieldEncoding.KeypadDial) return true;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return false;
        return number >= old.LowerBound && number <= old.UpperBound && number >= next.LowerBound && number <= next.UpperBound &&
            (old.Choices == null || old.Choices.Any(choice => choice.Value == number)) && (next.Choices == null || next.Choices.Any(choice => choice.Value == number));
    }
    private static bool ValidCredential(NativeTableField field, byte[] source)
    {
        if (field.Encoding != TableFieldEncoding.CredentialBcd || field.TextLimit is not (7 or 10)) return false;
        var packed = source.AsSpan(field.Offset, field.Width); var digits = field.TextLimit.Value;
        if ((digits & 1) != 0 && (packed[^1] & 15) != 14) return false;
        for (var index = 0; index < digits; index++) if (((packed[index / 2] >> ((index & 1) == 0 ? 4 : 0)) & 15) > 9) return false;
        return true;
    }
    private static string? Display(NativeTableField field, byte[] raw)
    {
        if (Protected(field)) return null;
        var value = field.Read(raw); if (value == null) return "Unrecognized";
        if (field.Encoding == TableFieldEncoding.Flag) return value == "true" ? "On" : "Off";
        if (field.Choices == null) return value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? field.Choices.SingleOrDefault(choice => choice.Value == number)?.Label ?? $"Unrecognized ({number})" : "Unrecognized";
    }
    private static ReadOnlySpan<byte> ScreenRow(byte[] record, int slot) => record.AsSpan(1 + (slot - 1) * 17, 17);
    private static bool MatchingScreening(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, byte[]? fromRates, byte[]? toRates,
        byte[]? fromCarriers, byte[]? toCarriers)
    {
        if ((source[1] & 15) is > 11 and < 15 || (source[1] >> 4) > 7 ||
            !source[..2].SequenceEqual(target[..2]) || !source.Slice(3, 9).SequenceEqual(target.Slice(3, 9)) ||
            (source[13] & 3) != (target[13] & 3) || (target[13] & 0x5C) != 0) return false;
        var a = source[2]; var b = target[2];
        if (a == byte.MaxValue || b == byte.MaxValue || (source[1] & 15) == 11) return a == b;
        if ((source[0] & 0x81) == 0x81)
            return a == b && (a == 0 || UniqueCarrier(fromCarriers, 23, 678, 4, 32, 21, a) && UniqueCarrier(toCarriers, 135, 1109, 10, 33, 33, b));
        if ((source[0] & 1) != 0 || a >= 128 || b >= 128 || fromRates is not { Length: 1192 } || fromRates[0] != 73 ||
            toRates is not { Length: 1192 } || toRates[0] != 73) return false;
        return fromRates[40 + a * 9] <= 8 && fromRates.AsSpan(40 + a * 9, 9).SequenceEqual(toRates.AsSpan(40 + b * 9, 9));
    }
    private static bool UniqueCarrier(byte[]? raw, int number, int length, int header, int stride, int rows, byte reference) =>
        raw != null && raw.Length == length && raw[0] == number && Enumerable.Range(0, rows).Count(row => raw[header + row * stride] == reference) == 1;
}
