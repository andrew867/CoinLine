using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record TableFirmwareFinding(int TableNumber, bool Blocking, string Message);

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
            else if (profile == null || !profile.VerifiedLengths.TryGetValue(number, out var length))
                yield return new(number, false, "The record format has no verified length for the selected firmware.");
            else if (data.Length != length)
                yield return new(number, true, $"The selected firmware uses {length:N0} bytes. The record has {data.Length:N0}.");
            else
                verifiedRecords.Add(number, data);
        }

        if (profile?.Id == FirmwareTableProfiles.MultiPay200)
            foreach (var finding in ReviewMultiPayRates(verifiedRecords))
                yield return finding;
    }

    private static IEnumerable<TableFirmwareFinding> ReviewMultiPayRates(IReadOnlyDictionary<int, byte[]> records)
    {
        const int tariffType = 73;
        const int screeningType = 92;
        const int localExchangeType = 101;
        const int screeningRowSize = 17;
        const int tariffCount = 128;
        const int deniedCall = 11;
        const int freeFlag = 1;
        const int carrierRoute = 4;
        const int fallbackRoute = 8;
        const int featureGroupB = 128;

        var usesDirectTariff = false;
        if (records.TryGetValue(screeningType, out var screening))
        {
            for (var offset = 1; offset < screening.Length; offset += screeningRowSize)
            {
                var flags = screening[offset];
                var classification = screening[offset + 1];
                var call = classification & 15;
                var payment = (classification >> 4) & 7;
                var rate = screening[offset + 2];
                var firstDigit = screening[offset + 4] >> 4;
                if (firstDigit == 0 || call == deniedCall || (flags & (fallbackRoute | featureGroupB)) != 0)
                    continue;

                var row = (offset - 1) / screeningRowSize + 1;
                if ((flags & freeFlag) != 0)
                {
                    if (rate != byte.MaxValue)
                        yield return new(screeningType, true, $"Screening rule {row} is marked free. Set its rate index to 255 for an unrated call.");
                    continue;
                }

                if ((flags & carrierRoute) != 0 || payment < 3)
                    continue;
                if (rate >= tariffCount)
                    yield return new(screeningType, true, $"Screening rule {row} references tariff {rate}. MT73 has tariff indices 0 to 127.");
                else
                    usesDirectTariff = true;
            }
        }

        var usesExchangeTariff = records.TryGetValue(localExchangeType, out var exchange) && (exchange[1] != 0 || (exchange[2] & 0xF0) != 0);
        if ((usesDirectTariff || usesExchangeTariff) && !records.ContainsKey(tariffType))
            yield return new(tariffType, false, "Screening or local exchange rules refer to MT73 tariffs. This set relies on the terminal's existing MT73 record.");
    }
}
