using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record TableFirmwareFinding(int TableNumber, bool Blocking, string Message);
public sealed record TableRecordShape(int TableNumber, int Length, string? ConfirmedLayout);

public static class TableSetFirmwareCompatibility
{
    public static string? Mismatch(TableSet set, Guid? installedVersionId)
    {
        if (set.TargetFirmwareVersionId is not { } target || target == installedVersionId)
            return null;

        var label = set.TargetFirmwareVersion?.Label;
        var requirement = label == null ? "the set's target firmware" : label;
        return installedVersionId == null
            ? $"The phone's firmware is unknown. This set requires {requirement}."
            : $"The phone's registered firmware differs from {requirement}.";
    }

    public static IEnumerable<TableFirmwareFinding> Review(TableSet set)
    {
        if (set.TargetFirmwareVersionId == null)
            yield break;

        var profile = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        var verifiedRecords = new Dictionary<int, byte[]>();
        foreach (var record in set.Versions.GroupBy(v => v.TableDefinition?.TableNumber ?? 0).OrderBy(g => g.Key))
        {
            var number = record.Key;
            if (record.Count() != 1)
            {
                yield return new(number, true, "The set contains multiple records of this type.");
                continue;
            }

            var data = TableDistributionService.GetEffectivePayload(record.Single());
            if (data.Length <= 2 || data[0] != number)
                yield return new(number, true, "Import a complete record with the matching message type.");
            else if (RecordIssue(profile, number, data.Length) is { } issue)
                yield return new(number, true, issue);
            else if (profile == null || !profile.VerifiedLengths.ContainsKey(number))
                yield return new(number, false, "The record format has no verified length for the selected firmware.");
            else
            {
                verifiedRecords.Add(number, data);
                if (profile.ObservedLengthsOnly && NativeTableLayouts.Candidates(number, data).Count > 1)
                    yield return new(number, false, "The record length was acknowledged by an emulated terminal, but the native field layout is not established for this firmware. Confirm the layout before editing.");
            }
        }

        if (profile?.Id is FirmwareTableProfiles.MultiPay200 or FirmwareTableProfiles.LaterMultiPay180
            or FirmwareTableProfiles.GraphicalMultiPay180)
        {
            var later = profile.Id != FirmwareTableProfiles.MultiPay200;
            if (verifiedRecords.TryGetValue(NumberingPlanWire.MessageType, out var numbering))
                foreach (var finding in NumberingPlanWire.Review(numbering, later))
                    yield return new(NumberingPlanWire.MessageType, finding.Blocking, finding.Message);
            foreach (var finding in ReviewServiceScreening(verifiedRecords, later))
                yield return finding;
            foreach (var finding in ReviewCarrierRoutes(verifiedRecords, later))
                yield return finding;
            foreach (var finding in ReviewServiceRestriction(verifiedRecords, later, profile.Id == FirmwareTableProfiles.GraphicalMultiPay180))
                yield return finding;
            foreach (var number in new[] { CollectionPolicyWire.CommunicationsType, CollectionPolicyWire.CallCarrierType })
                if (verifiedRecords.TryGetValue(number, out var collection))
                    foreach (var finding in CollectionPolicyWire.Review(collection, later))
                        yield return new(number, finding.Blocking, finding.Message);
            if (verifiedRecords.TryGetValue(MessagingServiceWire.MessageType, out var messaging))
            {
                if (MessagingServiceWire.ConfigurationIssue(messaging) is { } messagingIssue)
                    yield return new(MessagingServiceWire.MessageType, true, messagingIssue);
                yield return new(MessagingServiceWire.MessageType, false,
                    "Audio-Gram is a conditional firmware feature. The matching record length does not establish that this terminal build accepts MT68 or supports the switch-provided service.");
            }
            foreach (var finding in ReviewMultiPayRates(verifiedRecords, later))
                yield return finding;
            if (profile.Id == FirmwareTableProfiles.GraphicalMultiPay180
                && verifiedRecords.Keys.Any(number => number is >= 131 and <= 133))
            {
                var review = NativeTableLayouts.ReviewGraphicalLinks(verifiedRecords);
                if (review.Records.Single(record => record.Number == 131).Availability != "included")
                    yield return new(verifiedRecords.Keys.Where(number => number is >= 132 and <= 133).Min(), false,
                        "The MT131 rotation header is not included. The terminal may retain its previous graphical rotation settings.");
                foreach (var group in review.References.Where(link => link.Advisory != null && link.Role != "disabled_context")
                    .GroupBy(link => link.SourceRecord))
                    yield return new(group.Key, false,
                        $"{group.Count()} saved graphical reference(s) need review. Open the graphical link review for exact sources, destinations and partial-set context.");
            }
        }
    }

    private static IEnumerable<TableFirmwareFinding> ReviewServiceRestriction(IReadOnlyDictionary<int, byte[]> records, bool later, bool graphical)
    {
        if (!records.TryGetValue(ServiceRestrictionWire.MessageType, out var restriction))
            yield break;
        if (ServiceRestrictionWire.ConfigurationIssue(restriction, later) is { } issue)
        {
            yield return new(ServiceRestrictionWire.MessageType, true, issue);
            yield break;
        }
        if (restriction[1] != ServiceRestrictionWire.ConfigurableAccess)
            yield break;

        var expected = graphical ? new[] { 55, 92, 131, 132, 133 } : new[] { 55, 92 };
        var reviews = expected.Where(records.ContainsKey)
            .Select(number => ServiceRestrictionWire.ReadRestrictedRoutes(number, records[number])).ToArray();
        var candidates = reviews.SelectMany(review => review.Routes).Where(route => route.Dialable).ToArray();
        if (candidates.Length > 0)
        {
            if (graphical && candidates.Any(route => route.FreeCall == false))
                yield return new(ServiceRestrictionWire.MessageType, false,
                    "A marked restricted route is paid. The graphical terminal configuration guidance specifies free restricted routes; verify the configured payment behavior on device.");
            yield break;
        }

        if (reviews.Length == expected.Length)
            yield return new(ServiceRestrictionWire.MessageType, false, graphical
                ? "Configurable restricted access is enabled, but no included dialable screening entry, quick-access key or graphical key is marked as permitted."
                : "Configurable restricted access is enabled, but no included dialable screening entry or quick-access key is marked as permitted. Review the permitted routes before applying this window.");
        else
        {
            var missing = string.Join(" and ", expected.Where(number => !records.ContainsKey(number)).Select(number => $"MT{number}"));
            yield return new(ServiceRestrictionWire.MessageType, false,
                $"Configurable restricted access relies on permitted entries in the phone's existing {missing} record(s). No permitted dialable entry was found in the included records.");
        }

    }

    private static IReadOnlyList<TableFirmwareFinding> ReviewCarrierRoutes(IReadOnlyDictionary<int, byte[]> records, bool later)
    {
        const int freeFeatureGroupB = 0x81;
        const int informationKey = 0x40;
        const int deniedCall = 11;
        var carrierType = later ? 135 : 23;
        var header = later ? 10 : 4;
        var stride = later ? 33 : 32;
        var count = later ? 33 : 21;
        var findings = new List<TableFirmwareFinding>();
        var dependencies = new SortedSet<int>();
        records.TryGetValue(carrierType, out var carriers);
        var references = new HashSet<byte>();
        if (carriers != null)
            for (var offset = header; offset < header + count * stride; offset += stride)
                references.Add(carriers[offset]);

        if (records.TryGetValue(92, out var screening))
            for (var offset = 1; offset < screening.Length; offset += 17)
                if ((screening[offset] & freeFeatureGroupB) == freeFeatureGroupB &&
                    (screening[offset + 1] & 15) != deniedCall && (screening[offset + 4] >> 4) != 0)
                    ReviewReference(92, $"Screening rule {(offset - 1) / 17 + 1}", screening[offset + 2]);

        if (records.TryGetValue(55, out var keys))
            for (var offset = 1; offset < keys.Length; offset += QuickAccessKeyWire.SlotLength)
                if ((keys[offset] & (freeFeatureGroupB | informationKey)) == freeFeatureGroupB &&
                    (keys[offset + 1] & 15) != deniedCall && (keys[offset + 3] >> 4) != 0)
                    ReviewReference(55, $"Quick-access key {(offset - 1) / QuickAccessKeyWire.SlotLength + 1}", keys[offset + 2]);

        if (dependencies.Count > 0)
            findings.Add(new(carrierType, false, $"{string.Join(", ", dependencies.Select(number => $"MT{number}"))} select Feature Group B carrier references. This set relies on the terminal's existing MT{carrierType} record."));
        return findings;

        void ReviewReference(int number, string label, byte reference)
        {
            // No carrier is a supported selection, and zero can represent the local exchange.
            if (reference is 0 or byte.MaxValue)
                return;
            if (carriers == null)
                dependencies.Add(number);
            else if (!references.Contains(reference))
                findings.Add(new(number, false, $"{label} selects Feature Group B carrier reference {reference}, which is missing from MT{carrierType}. This field is a carrier identifier, not a tariff index or row position."));
        }
    }

    public static string? RecordIssue(FirmwareTableProfile? profile, int number, int length)
    {
        if (profile == null)
            return null;
        if (profile.VerifiedLengths.TryGetValue(number, out var expected))
            return length != expected
                ? $"The selected firmware uses {expected:N0} bytes. The record has {length:N0}." : null;
        return profile.CompleteCatalogue ? "This record type is absent from the selected firmware catalogue." : null;
    }

    public static IEnumerable<TableRecordShape> Shapes(IEnumerable<TableVersion> records) =>
        records.Where(record => record.TableDefinition != null).Select(record => new TableRecordShape(
            record.TableDefinition!.TableNumber, TableDistributionService.GetEffectivePayload(record).Length,
            record.ScreeningLayoutProfile));

    // Unknown or incomplete profiles do not prove that an unlisted record is
    // unsupported. Reject only contradictions with the known installed profile.
    public static string? SelectionIssue(FirmwareTableProfile? profile, IEnumerable<TableRecordShape> records)
    {
        if (profile == null)
            return null;
        foreach (var record in records.OrderBy(record => record.TableNumber))
        {
            if (RecordIssue(profile, record.TableNumber, record.Length) is { } issue)
                return $"MT{record.TableNumber}. {issue}";
            if (record.ConfirmedLayout != null && profile.VerifiedLengths.ContainsKey(record.TableNumber)
                && profile.LayoutFor(record.TableNumber) is { } expected && record.ConfirmedLayout != expected)
                return $"MT{record.TableNumber}. The confirmed record layout differs from the phone's registered firmware layout.";
        }
        return null;
    }

    private static IReadOnlyList<TableFirmwareFinding> ReviewServiceScreening(IReadOnlyDictionary<int, byte[]> records, bool later)
    {
        const int screeningType = 92;
        const int screeningRowSize = 17;
        var slotCount = later ? 180 : 200;
        records.TryGetValue(screeningType, out var screening);
        var findings = new List<TableFirmwareFinding>();
        var omittedScreeningSources = new SortedSet<int>();

        if (records.TryGetValue(26, out var features))
        {
            if ((features[5] & 16) != 0)
            {
                ReviewOperator(48, "Intra-area alternate operator access");
                ReviewOperator(53, "Basic alternate operator access");
                if (later)
                {
                    if (features[16] != 0) ReviewOperator(16, "Inter-area alternate operator access");
                    if (features[17] != 0) ReviewOperator(17, "International alternate operator access");
                }
            }

            if (later)
            {
                if (features[15] != 0) ReviewOperator(15, "International operator reroute");
                if (features[20] != 0) ReviewOperator(20, "Intra-area operator reroute");
                if (features[21] != 0) ReviewOperator(21, "Inter-area operator reroute");
            }

            void ReviewOperator(int offset, string name)
            {
                if (features[offset] == 0)
                    findings.Add(new(26, false, $"{name} is unset while alternate operator service is enabled. Confirm the dialing rules cannot select this route, or choose an MT92 entry."));
                else
                    ReviewReference(26, name, features[offset], required: true, literalRequired: true);
            }
        }

        var carrierType = later ? 135 : 23;
        if (records.TryGetValue(carrierType, out var carriers))
        {
            var start = later ? 10 : 4;
            var stride = later ? 33 : 32;
            var count = later ? 33 : 21;
            for (var index = 0; index < count; index++)
            {
                var offset = start + index * stride;
                if (carriers[offset] == byte.MaxValue || (carriers[offset + stride - 5] & 128) == 0)
                    continue;
                ReviewReference(carrierType, $"Carrier {index} Feature Group B", carriers[offset + stride - 1], required: true, literalRequired: false);
            }
        }

        if (later && records.TryGetValue(134, out var cards))
        {
            for (var index = 0; index < 32; index++)
            {
                var offset = 1 + index * 36;
                if (cards[offset + 6] is < 10 or > 16 || (cards[offset + 33] & 0x12) == 0)
                    continue;
                var reference = cards[offset + 34];
                var label = $"Stored-value card range {index + 1} reload";
                if (reference > slotCount)
                    findings.Add(new(134, false, $"{label} references entry {reference}. This firmware defaults an out-of-range reload reference to entry 1."));
                ReviewReference(134, label, reference is 0 || reference > slotCount ? 1 : reference, required: false, literalRequired: false);
            }
        }

        if (later && records.TryGetValue(SpareTableWire.MessageType, out var spare) && spare[345] != 0)
        {
            ReviewReference(SpareTableWire.MessageType, "IOPR access", spare[345], required: true, literalRequired: true);
            findings.Add(new(SpareTableWire.MessageType, false, "IOPR is configured. The packed layout is known, but this feature requires an IOPR-enabled firmware build and compatible terminal mode."));
        }

        if (omittedScreeningSources.Count > 0)
            findings.Add(new(screeningType, false, $"{string.Join(", ", omittedScreeningSources.Select(number => $"MT{number}"))} refer to call-screening entries. This set relies on the terminal's existing MT92 record."));
        return findings;

        void ReviewReference(int source, string label, int reference, bool required, bool literalRequired)
        {
            if (reference < 1 || reference > slotCount)
            {
                findings.Add(new(source, required, $"{label} references entry {reference}. Choose an MT92 entry from 1 to {slotCount}."));
                return;
            }
            if (screening == null)
            {
                omittedScreeningSources.Add(source);
                return;
            }
            var offset = 1 + (reference - 1) * screeningRowSize + 4;
            var dialField = new NativeTableField("number", "Access number", offset, TableFieldEncoding.KeypadDial, 8);
            var number = dialField.Read(screening);
            if (number == "")
                findings.Add(new(source, required, $"{label} points to blank MT92 entry {reference}. Choose an entry with an access number."));
            else if (number == null)
                findings.Add(new(source, literalRequired, $"{label} points to MT92 entry {reference}, which does not contain a literal dialable number. Review the access pattern."));
        }
    }

    private static IEnumerable<TableFirmwareFinding> ReviewMultiPayRates(IReadOnlyDictionary<int, byte[]> records, bool later)
    {
        const int tariffType = 73;
        const int screeningType = 92;
        const int quickAccessType = 55;
        const int screeningRowSize = 17;
        const int tariffCount = 128;
        const int deniedCall = 11;
        const int freeFlag = 1;
        const int carrierRoute = 4;
        const int fallbackRoute = 8;
        const int featureGroupB = 128;

        var tariffSources = new SortedSet<int>();
        records.TryGetValue(26, out var features);
        if (records.TryGetValue(screeningType, out var screening))
        {
            for (var offset = 1; offset < screening.Length; offset += screeningRowSize)
            {
                var flags = screening[offset];
                var classification = screening[offset + 1];
                var call = classification & 15;
                var payment = classification >> 4;
                var rate = screening[offset + 2];
                var firstDigit = screening[offset + 4] >> 4;
                if (firstDigit == 0 || call == deniedCall || (flags & (fallbackRoute | featureGroupB)) != 0)
                    continue;

                var row = (offset - 1) / screeningRowSize + 1;
                if ((flags & freeFlag) != 0)
                {
                    if (!later && rate != byte.MaxValue)
                        yield return new(screeningType, true, $"Screening rule {row} is marked free. Set its rate index to 255 for an unrated call.");
                    else if (later && call == 9 && rate != byte.MaxValue && (flags & carrierRoute) == 0 && (screening[offset + 13] & 2) == 0 &&
                             (features == null || (features[54] & 1) != 0))
                        yield return new(screeningType, false, $"Screening rule {row} is free for voice calls, but a toll-free data-port call may be rated. Use index 255 if it should also be free.");
                    continue;
                }

                if ((flags & carrierRoute) != 0 || !IsPaidClassification(payment, later))
                    continue;
                if (rate >= tariffCount)
                    yield return new(screeningType, true, $"Screening rule {row} references tariff {rate}. MT73 has tariff indices 0 to 127.");
                else
                    tariffSources.Add(screeningType);
            }
        }

        if (records.TryGetValue(quickAccessType, out var keys))
        {
            const int informationKey = 64;
            for (var offset = 1; offset < keys.Length; offset += QuickAccessKeyWire.SlotLength)
            {
                var classification = keys[offset + 1];
                if ((keys[offset + 3] >> 4) == 0 || (classification & 15) == deniedCall ||
                    (keys[offset] & (freeFlag | carrierRoute | featureGroupB | informationKey)) != 0 ||
                    !IsPaidClassification(classification >> 4, later))
                    continue;
                var key = (offset - 1) / QuickAccessKeyWire.SlotLength + 1;
                var rate = keys[offset + 2];
                if (rate >= tariffCount)
                    yield return new(quickAccessType, true, $"Quick-access key {key} references tariff {rate}. MT73 has tariff indices 0 to 127.");
                else
                    tariffSources.Add(quickAccessType);
            }
        }

        var areaField = new NativeTableField("areaCode", "Area code", 1, TableFieldEncoding.LocalDialingArea);
        var exchangeAreas = new Dictionary<string, int>();
        foreach (var (number, exchange) in records.Where(record => later
                     ? record.Key is >= 136 and <= 149 or 154 or 155
                     : record.Key is >= 101 and <= 107))
        {
            if (exchange[1] == 0 && (exchange[2] & 0xF0) == 0)
                continue;
            tariffSources.Add(number);
            var area = areaField.Read(exchange);
            if (area == null)
                yield return new(number, false, "The local dialing area is not a recognized three-digit area code. Check the imported record before using it.");
            else if (!exchangeAreas.TryAdd(area, number))
                yield return new(number, false, $"Area code {area} is also configured in MT{exchangeAreas[area]}. The phone searches the local dialing segments in record order.");
        }

        if (later)
        {
            if (features != null && (features[13] & 1) != 0 && !records.ContainsKey(150))
                yield return new(150, false, "MT26 enables area-code set-based rating. This set relies on the terminal's existing MT150 record.");
            if (features != null && (features[13] & 2) != 0 && !records.ContainsKey(151))
                yield return new(151, false, "MT26 enables international set-based rating. This set relies on the terminal's existing MT151 record.");

            if (records.TryGetValue(150, out var areaRates) && areaRates.Skip(1).Any(value => (value >> 4) > 1 || (value & 15) > 1))
                tariffSources.Add(150);

            if (records.TryGetValue(151, out var international))
            {
                var usesDefault = (international[1] & 3) == 1;
                if (usesDefault && international[2] >= 100)
                    yield return new(151, true, "The international default rate must be 0 to 99. A larger value can select a tariff outside MT73.");
                else if (usesDefault && international[2] >= 2)
                    tariffSources.Add(151);

                var usesFallback = false;
                for (var offset = 4; offset < international.Length; offset += 3)
                {
                    if (international[offset] == 0 && international[offset + 1] == 0)
                        continue;
                    var rate = international[offset + 2];
                    if (rate is >= 2 and <= 99)
                        tariffSources.Add(151);
                    else if (rate >= 100)
                        usesFallback = true;
                }
                if (usesFallback && usesDefault && international[2] < 2)
                {
                    tariffSources.Add(151);
                    yield return new(151, false, "An international destination uses the fallback action with default code 0 or 1. For such destinations this firmware reads MT73 rate 28 or 29 instead of applying the usual host-rating or reject sentinel.");
                }
            }
        }

        if (tariffSources.Count > 0 && !records.ContainsKey(tariffType))
            yield return new(tariffType, false, $"{string.Join(", ", tariffSources.Select(number => $"MT{number}"))} refer to MT73 tariffs. This set relies on the terminal's existing MT73 record.");
    }

    private static bool IsPaidClassification(int payment, bool later) =>
        later ? payment is >= 3 and <= 11 : (payment & 7) >= 3;
}
