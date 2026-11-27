using System.Globalization;

namespace HostPlatform.Protocols.Tables;

public sealed record NumberingTransitionField(string Key, string Label, string Action, bool Protected,
    string? SourceValue, string? TargetBefore, string? TargetAfter);
public sealed record NumberingTransitionProjection(byte[] Payload, NumberingTransitionField[] Fields, NumberingGraphReview[] Graphs,
    int ChangedBytes, NumberingPlanFinding[] Findings);

/// <summary>Prepare the earlier numbering behavior in a later candidate without copying its profile-specific site/currency bytes.</summary>
public static class NumberingTransitionWire
{
    public static NumberingTransitionProjection Project(byte[] source, byte[] target, bool carryHeader = true, bool carryDial = true,
        bool carryFormat = true, bool carryDeskCredential = false)
    {
        var old = NativeTableLayouts.Candidates(62,source).SingleOrDefault(layout => layout.Profile == NativeTableLayouts.MultiPay);
        var later = NativeTableLayouts.Candidates(62,target).SingleOrDefault(layout => layout.Profile == NativeTableLayouts.Later);
        if (old == null || later == null) throw new InvalidOperationException("Choose identified earlier and later MT62 layouts.");
        var graph = NumberingPlanWire.CarryForwardGraphs(source,target,carryDial,carryFormat);
        var output = graph.Payload; var findings = graph.Findings.ToList();
        var fields = new List<NumberingTransitionField>();
        var targetFields = later.Sections.SelectMany(section => section.Fields).Where(field => !field.IsSpare).ToDictionary(field => field.Key);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in old.Sections.SelectMany(section => section.Fields).Where(field => !field.IsSpare))
        {
            var secret = field.IsCredential;
            if (!targetFields.TryGetValue(field.Key,out var next) || !SameShape(field,next))
            {
                fields.Add(new(field.Key,field.Label,"source-only",secret,Display(field,source),null,null)); continue;
            }
            seen.Add(field.Key); var action = "unchanged";
            var ordinary = field.Offset is >= 1 and < 19 || field.Offset == 32;
            if (field.IsCredential)
            {
                if (!carryDeskCredential) action = "retained-target";
                else if (!CanonicalDesk(field,source))
                {
                    if (Equal(field,source,target)) findings.Add(new(false,"The identical desk key-card field is retained but its packed format is not established."));
                    else { Block("The source desk key-card field is unrecognized. Retain the prepared target credential or review the source first."); action = "unresolved"; }
                }
                else Copy(field,source,output);
            }
            else if (!ordinary || !carryHeader) action = "retained-target";
            else if (!ValidShared(field,next,source))
            {
                if (Equal(field,source,target)) findings.Add(new(false,$"An identical {field.Label.ToLowerInvariant()} value is unrecognized by the typed editor and remains unchanged."));
                else { Block($"The source {field.Label.ToLowerInvariant()} value has no recognized common representation. Review it or retain the prepared target header."); action = "unresolved"; }
            }
            else Copy(field,source,output);
            if (action == "unchanged" && !Equal(next,target,output)) action = "changed";
            fields.Add(new(field.Key,field.Label,action,secret,Display(field,source),Display(next,target),Display(next,output)));
        }
        foreach (var field in targetFields.Values.Where(field => !seen.Contains(field.Key)))
            fields.Add(new(field.Key,field.Label,"target-only",field.IsCredential,null,Display(field,target),Display(field,output)));
        findings.Add(new(false,"Earlier answer-supervision, manager-timeout and currency fields are not reinterpreted as later spare or display-format bytes. Review the target behavior separately."));
        findings.Add(new(false,"Profile-specific stored-value discount fields remain as supplied by the target. The desk key-card field is carried only when explicitly selected; its value never appears in the review."));
        findings.Add(new(false,"The selected source graphs replace recognized candidate nodes while preserving unknown candidate bytes. Ordinary routes, rate-response classifications, and terminal format behavior still require target device qualification."));
        var changed = output.Zip(target).Count(pair => pair.First != pair.Second);
        return new(output,fields.ToArray(),graph.Graphs,changed,findings.Distinct().ToArray());
        void Block(string message) => findings.Add(new(true,message));
    }

    private static bool SameShape(NativeTableField a, NativeTableField b) => a.Offset == b.Offset && a.Width == b.Width && a.Encoding == b.Encoding && a.Mask == b.Mask;
    private static bool Masked(NativeTableField field) => field.Encoding is TableFieldEncoding.Flag or TableFieldEncoding.Bits;
    private static bool Equal(NativeTableField field,byte[] a,byte[] b) => Masked(field) ? (a[field.Offset]&field.Mask) == (b[field.Offset]&field.Mask)
        : a.AsSpan(field.Offset,field.Width).SequenceEqual(b.AsSpan(field.Offset,field.Width));
    private static void Copy(NativeTableField field,byte[] source,byte[] target)
    {
        if (Masked(field)) target[field.Offset] = (byte)((target[field.Offset] & ~field.Mask) | (source[field.Offset] & field.Mask));
        else source.AsSpan(field.Offset,field.Width).CopyTo(target.AsSpan(field.Offset,field.Width));
    }
    private static bool ValidShared(NativeTableField a,NativeTableField b,byte[] source)
    {
        var value = a.Read(source); if (value == null) return false;
        if (a.Encoding == TableFieldEncoding.Flag) return true;
        return long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var number) && number >= a.LowerBound && number <= a.UpperBound &&
            number >= b.LowerBound && number <= b.UpperBound && (a.Choices == null || a.Choices.Any(choice => choice.Value == number)) &&
            (b.Choices == null || b.Choices.Any(choice => choice.Value == number));
    }
    private static bool CanonicalDesk(NativeTableField field,byte[] record)
    {
        if (field.Encoding != TableFieldEncoding.CredentialBcd || field.TextLimit != 10 || field.Width != 5) return false;
        foreach (var value in record.AsSpan(field.Offset,field.Width)) if ((value >> 4) > 9 || (value & 15) > 9) return false;
        return true;
    }
    private static string? Display(NativeTableField field,byte[] record)
    {
        if (field.IsCredential) return null;
        var value = field.Read(record); if (value == null) return "Unrecognized";
        if (field.Encoding == TableFieldEncoding.Flag) return value == "true" ? "On" : "Off";
        if (field.Choices == null) return value;
        return int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var number)
            ? field.Choices.SingleOrDefault(choice => choice.Value == number)?.Label ?? $"Unrecognized ({number})" : "Unrecognized";
    }
}
