using System.Buffers.Binary;

namespace HostPlatform.Protocols.Tables;

public sealed record CommonControlGroup(string Id, int Number, string Label, string Note);
public sealed record CommonControlField(string Key, string Label, string? Source, string? Target, string Status);
public sealed record CommonControlGroupReview(CommonControlGroup Group, bool Selected, CommonControlField[] Fields);
public sealed record CommonControlFinding(bool Blocking, int Number, string Message);
public sealed record CommonControlProjection(byte[] Payload, CommonControlGroupReview[] Groups, int ChangedBytes, CommonControlFinding[] Findings);

/// <summary>Explicitly selected common configuration fields, with candidate-specific bytes retained.</summary>
public static class CommonControlTransitionWire
{
    public static IReadOnlyList<CommonControlGroup> Groups { get; } =
    [
        new("text-advertising", 29, "Text advertising messages", "Carry the defined twenty-slot text rotation, not target graphical artwork or MT26 display enablement. Reserved slot controls stay on the candidate."),
        new("ui-prompts", 30, "Prompt and input timing", "Review display and voice timing on the target hardware."),
        new("ui-calls", 30, "Card, rating and dialing timers", "Review the configured timeouts in service."),
        new("ui-assisted", 30, "Operator and carrier access timing", "Review the intended carrier and access behavior."),
        new("ui-data-port", 30, "Data port timing", "Select only if the target includes the intended data-port hardware and configuration."),
        new("communications", 32, "Communications statistics", "The reserved timing pair is retained; review the resulting collection schedule and histogram bounds."),
        new("modem-line-timing", 33, "Modem speed and shared line timing", "Review speed, call progress and turnaround times against the target modem. The later answer-tone removal timer is retained; the earlier spare word is never interpreted as a timer."),
        new("modem-seizure", 33, "Modem line seizure method", "Carry the configured ground-start choice only after checking the target line interface."),
        new("modem-dial-detection", 33, "Dial, busy and ringback detection", "Carry both detector debounce triplets and the busy threshold. Review against the target line and modem."),
        new("modem-answer-detection", 33, "Modem answer-tone detection", "Carry the answer debounce and shared detection, optional-answer and removal-wait choices. The candidate's separate answer-removal timer remains unchanged."),
        new("modem-carrier-detection", 33, "Modem carrier and loss detection", "Carry the carrier and lost-carrier debounce triplets and the carrier-loss timeout switch. Review the complete target modem exchange."),
        new("call-collection", 34, "Call statistics and CDR logging", "Carries the defined collection schedule and CDR policy, independently of carrier statistics."),
        new("carrier-collection", 34, "Carrier statistics mode", "Explicitly maps earlier disabled or per-carrier mode to the later field. If selected, the prepared combined-summary mode is replaced."),
        new("routine-call-in", 35, "Routine call-in and upload threshold", "Carries time of day, interval, retries and threshold. Candidate start date and one-time clock adjustment are retained."),
        new("coin-denominations", 50, "Coin values and acceptance", "The candidate's validator window enablement and volume calibration are retained unless separately selected."),
        new("coin-hardware", 50, "Validator windows and physical capacities", "Select only after checking the physical validator, coin volumes, cashbox and escrow hardware."),
        new("coin-collection", 50, "Cashbox and escrow reporting thresholds", "Review these thresholds against the prepared target capacities. Existing cashbox status and collection history are not configuration fields."),
        new("daily-service-restriction", 58, "Daily service restriction", "Carry the earlier restriction level and daily local-time window. Candidate-only configurable route permissions and reserved bytes are retained, and must be reviewed independently."),
        .. Enumerable.Range(0, 15).Select(index => new CommonControlGroup($"stored-value-unit-{index}", 93, $"Stored-value unit limit, index {index}",
            "Carry only this index's maximum units and multiplier. Confirm the target card-value mapping independently; validation data, discounts, other unit indexes and issuer settings are not carried."))
    ];

    public static int? LengthFor(int number) => number switch { 29 => AdvertisingTableWire.RecordLength, 30 => 68, 32 => 33, 33 => 36, 34 => 19, 35 => 21, 50 => 105, 58 => ServiceRestrictionWire.RecordLength, 93 => 225, _ => null };

    public static CommonControlProjection Project(int number, byte[] source, byte[] target, IReadOnlyCollection<string> selections)
    {
        var length = LengthFor(number);
        if (length == null || source.Length != length || target.Length != length || source[0] != number || target[0] != number)
            throw new InvalidOperationException("Choose complete matching supported common-control records.");
        var groups = Groups.Where(group => group.Number == number).ToArray();
        if (selections.Distinct(StringComparer.Ordinal).Count() != selections.Count || selections.Any(id => !groups.Any(group => group.Id == id)))
            throw new InvalidOperationException("Choose each applicable common-control group once.");
        var output = target.ToArray(); var reviews = new List<CommonControlGroupReview>(); var findings = new List<CommonControlFinding>();
        if (number == 29)
        {
            var group = groups.Single(); var selected = selections.Contains(group.Id);
            reviews.Add(new(group, selected, Advertising(source, target, output, selected, findings)));
        }
        else
        {
            var earlier = NativeTableLayouts.Find(number, source, NativeTableLayouts.MultiPay)!;
            var later = NativeTableLayouts.Find(number, target, number is 33 or 34 or 58 or 93 ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay)!;
            var targetFields = later.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key, StringComparer.Ordinal);
            foreach (var group in groups)
            {
                var selected = selections.Contains(group.Id); var rows = new List<CommonControlField>();
                foreach (var field in GroupFields(group, earlier))
                {
                    if (!targetFields.TryGetValue(field.Key, out var next)) throw new InvalidOperationException("The common-control field layout is incomplete.");
                    var carrierMode = group.Id == "carrier-collection";
                    if (field.IsSpare || next.IsSpare || field.IsCredential || next.IsCredential || field.Offset != next.Offset || field.Width != next.Width ||
                        field.Encoding != next.Encoding || (!carrierMode && field.Mask != next.Mask))
                        throw new InvalidOperationException("The common-control field layout is not shared.");
                    var value = field.Read(source); var before = next.Read(target);
                    if (!carrierMode && ValueEqual(field, source, target))
                    {
                        rows.Add(new(field.Key, field.Label, Display(field, value), Display(next, before), "same")); continue;
                    }
                    var proposed = target.ToArray(); var valid = value != null;
                    if (valid)
                    {
                        try
                        {
                            next.Write(proposed, value!);
                            // The typed writer must produce the exact common value, not silently normalize a packed field.
                            if (!carrierMode && !ValueEqual(field, source, proposed)) valid = false;
                        }
                        catch (InvalidOperationException) { valid = false; }
                    }
                    var same = valid && ValueEqual(next, target, proposed);
                    rows.Add(new(field.Key, field.Label, Display(field, value), Display(next, before), !valid ? "unmapped" : same ? "same" : "different"));
                    if (!selected || same) continue;
                    if (!valid) { findings.Add(new(true, number, $"The source {field.Label.ToLowerInvariant()} has no recognized common representation. Review this group before carrying it.")); continue; }
                    Copy(next, proposed, output);
                }
                reviews.Add(new(group, selected, rows.ToArray()));
            }
        }
        if (selections.Count != 0)
        {
            if (number == 33)
                findings.Add(new(false, number, "Review the resulting target modem timing and detector choices against the actual line interface. The later answer-tone removal timer and reserved flag bits remain unchanged. Configuration preparation does not qualify carrier, management calls or code transfer."));
            if (number == 93)
                findings.Add(new(false, number, "Confirm that each selected unit index has the intended meaning in the target card-value mapping. This carries only numeric limits and multipliers; issuer and validation data, discounts and acceptance settings remain unchanged. It does not qualify stored-value payment."));
            if (number == 58)
            {
                if (ServiceRestrictionWire.ConfigurationIssue(source, later: false) is { } sourceIssue)
                    findings.Add(new(true, number, $"Earlier service restriction: {sourceIssue}"));
                if (ServiceRestrictionWire.ConfigurationIssue(output, later: true) is { } targetIssue)
                    findings.Add(new(true, number, $"Prepared target service restriction: {targetIssue}"));
                if (target[1] == ServiceRestrictionWire.ConfigurableAccess && output[1] != target[1])
                    findings.Add(new(false, number, "The candidate's configurable restricted-access level will be replaced. Its separately configured screening, quick-access and graphical permissions remain unchanged; review the resulting service policy."));
            }
            if (number is 32 or 34)
                findings.AddRange(CollectionPolicyWire.Review(output, later: true).Select(finding => new CommonControlFinding(finding.Blocking, number, finding.Message)));
            if (number == 50 && selections.Any(id => id is "coin-denominations" or "coin-hardware"))
                foreach (var slot in CoinValidationWire.ReadSlots(output)!.Where(slot => slot.Accepted && (!slot.ValidatorWindowEnabled || slot.ValueMinorUnits == null)))
                    findings.Add(new(true, number, $"Coin slot {slot.Number} is accepted without a positive value and enabled validator window. Review the denomination and hardware groups together."));
            if (number == 50 && selections.Any(id => id is "coin-collection" or "coin-hardware"))
            {
                var fields = NativeTableLayouts.Find(number, output, NativeTableLayouts.MultiPay)!.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
                CheckThreshold("cashBoxThreshold", "cashBoxVolume", "Cashbox"); CheckThreshold("escrowVolumeThreshold", "escrowVolume", "Escrow");
                void CheckThreshold(string threshold, string capacity, string label)
                {
                    if (int.TryParse(fields[threshold].Read(output), out var t) && int.TryParse(fields[capacity].Read(output), out var c) && t > c && c != 0)
                        findings.Add(new(false, number, $"The {label.ToLowerInvariant()} reporting volume threshold exceeds the configured capacity. Review the target hardware and reporting settings."));
                }
            }
        }
        var changed = output.Zip(target).Count(pair => pair.First != pair.Second);
        return new(output, reviews.ToArray(), changed, findings.Distinct().ToArray());
    }

    private static IEnumerable<NativeTableField> GroupFields(CommonControlGroup group, NativeTableLayout layout)
    {
        foreach (var section in layout.Sections)
        foreach (var field in section.Fields)
        {
            if (field.IsSpare || field.IsCredential) continue;
            var include = group.Id switch
            {
                "ui-prompts" => section.Key == "prompts", "ui-calls" => section.Key == "calls", "ui-assisted" => section.Key == "assisted", "ui-data-port" => section.Key == "dataPort",
                "communications" => section.Key != "reserved",
                "modem-line-timing" => field.Key is "baud" or "seizeDelay" or "dialToneDelay" or "busyCycle" or "answerDelay" or "laptopSeizeTime" or "carrierDelay" or "turnaroundDelay",
                "modem-seizure" => field.Key == "groundStart",
                "modem-dial-detection" => section.Key is "debounce-0" or "debounce-1" || field.Key == "busyThreshold",
                "modem-answer-detection" => section.Key == "debounce-2" || field.Key is "answerToneDetection" or "answerToneOptional" or "skipAnswerToneRemoval",
                "modem-carrier-detection" => section.Key is "debounce-3" or "debounce-4" || field.Key == "carrierLossTimeout",
                "call-collection" => field.Key != "carrierStatistics", "carrier-collection" => field.Key == "carrierStatistics",
                "routine-call-in" => field.Key is "startTime" or "intervalDays" or "intervalHours" or "intervalMinutes" or "retryMinutes" or "retrySeconds" or "recordThreshold",
                "coin-denominations" => field.Key.StartsWith("coin-", StringComparison.Ordinal) && (field.Key.EndsWith(".value", StringComparison.Ordinal) || field.Key.EndsWith(".accepted", StringComparison.Ordinal)),
                "coin-hardware" => (field.Key.StartsWith("coin-", StringComparison.Ordinal) && (field.Key.EndsWith(".volume", StringComparison.Ordinal) || field.Key.EndsWith(".window", StringComparison.Ordinal))) || field.Key is "cashBoxVolume" or "escrowVolume",
                "coin-collection" => field.Key is "cashBoxThreshold" or "cashBoxValue" or "escrowVolumeThreshold" or "escrowValue",
                "daily-service-restriction" => section.Key == "restriction",
                _ when group.Number == 93 => section.Key == group.Id["stored-value-".Length..],
                _ => false
            };
            if (include) yield return field;
        }
    }

    private static CommonControlField[] Advertising(byte[] source, byte[] target, byte[] output, bool selected, List<CommonControlFinding> findings)
    {
        var old = AdvertisingTableWire.Read(source); var next = AdvertisingTableWire.Read(target);
        var rows = new List<CommonControlField>();
        for (var index = 0; index < old.Length; index++)
        {
            var a = old[index]; var b = next[index]; var start = 1 + index * 24;
            var sameText = a.EndSequence == b.EndSequence && (a.EndSequence == true || (!a.ContainsUnmappedGlyphs && !b.ContainsUnmappedGlyphs && a.Text == b.Text));
            var same = sameText && a.DurationTicks == b.DurationTicks && a.Mode == b.Mode && a.BlankBefore == b.BlankBefore;
            var valid = !a.ContainsUnmappedGlyphs && !b.ContainsUnmappedGlyphs && (a.Mode <= 5 || a.Mode == b.Mode);
            rows.Add(new($"slot-{index + 1}", $"Text slot {index + 1}", Describe(a), Describe(b), !valid ? "unmapped" : same ? "same" : "different"));
            if (!selected || same) continue;
            if (!valid) { findings.Add(new(true, 29, $"Text slot {index + 1} has an unmapped display cell or mode. Review it in the individual advertising editor.")); continue; }
            source.AsSpan(start, 2).CopyTo(output.AsSpan(start, 2));
            output[start + 2] = (byte)((target[start + 2] & AdvertisingTableWire.ReservedFlagsMask) | (source[start + 2] & ~AdvertisingTableWire.ReservedFlagsMask));
            if (!sameText)
            {
                if (a.EndSequence == true) output[start + 4] = 0;
                else source.AsSpan(start + 4, 20).CopyTo(output.AsSpan(start + 4, 20));
            }
        }
        return rows.ToArray();
        static string Describe(AdvertisingSlot slot) => $"{(slot.EndSequence == true ? "End marker" : slot.ContainsUnmappedGlyphs ? "Unmapped cells" : slot.Text.Length == 0 ? "Blank text" : slot.Text)}; {slot.DurationTicks} ticks; mode {slot.Mode}; blank before {(slot.BlankBefore ? "on" : "off")}";
    }

    private static bool Masked(NativeTableField field) => field.Encoding is TableFieldEncoding.Flag or TableFieldEncoding.Bits;
    private static bool ValueEqual(NativeTableField field, byte[] one, byte[] two) => field.Encoding == TableFieldEncoding.BigEndianBits
        ? (BinaryPrimitives.ReadUInt16BigEndian(one.AsSpan(field.Offset, 2)) & field.Mask) == (BinaryPrimitives.ReadUInt16BigEndian(two.AsSpan(field.Offset, 2)) & field.Mask)
        : Masked(field) ? (one[field.Offset] & field.Mask) == (two[field.Offset] & field.Mask)
        : one.AsSpan(field.Offset, field.Width).SequenceEqual(two.AsSpan(field.Offset, field.Width));
    private static void Copy(NativeTableField field, byte[] source, byte[] target)
    {
        if (field.Encoding == TableFieldEncoding.BigEndianBits)
        {
            var to = target.AsSpan(field.Offset, 2);
            var from = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(field.Offset, 2));
            BinaryPrimitives.WriteUInt16BigEndian(to, (ushort)((BinaryPrimitives.ReadUInt16BigEndian(to) & ~field.Mask) | (from & field.Mask)));
        }
        else if (Masked(field)) target[field.Offset] = (byte)((target[field.Offset] & ~field.Mask) | (source[field.Offset] & field.Mask));
        else source.AsSpan(field.Offset, field.Width).CopyTo(target.AsSpan(field.Offset, field.Width));
    }
    private static string? Display(NativeTableField field, string? value) => value == null ? "Unrecognized" :
        field.Encoding == TableFieldEncoding.Flag ? value == "true" ? "On" : "Off" :
        field.Choices == null ? value : int.TryParse(value, out var number) ? field.Choices.FirstOrDefault(choice => choice.Value == number)?.Label ?? $"Unrecognized ({value})" : value;
}
