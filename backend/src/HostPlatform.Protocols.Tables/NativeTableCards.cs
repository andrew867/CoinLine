using static HostPlatform.Protocols.Tables.TableFieldEncoding;

namespace HostPlatform.Protocols.Tables;

public static partial class NativeTableLayouts
{
    private const int CardStandardOffset = 6;
    private const int CardDataOffset = 11;
    private const int CardDataLength = 20;
    private const int SpillStepsOffset = 21;
    private const int SpillStepCount = 8;
    private const int SpillTerminatorOffset = 29;
    private const int NoCarrier = byte.MaxValue;

    private enum CardDataKind { Magnetic, StoredValue, Unknown }

    private static NativeTableField CarrierReference(string key, string label, int offset) =>
        Field(key, label, offset, help: "Use 255 when no carrier is selected here.");

    private static void InitializePaymentRecord(Span<byte> record)
    {
        var later = record[0] is 134 or 135;
        if (record[0] is 22 or 134)
        {
            var stride = later ? 36 : 33;
            for (var offset = 33; offset < record.Length; offset += stride)
                record[offset] = NoCarrier;
            return;
        }

        var header = later ? 10 : 4;
        var rowSize = later ? 33 : 32;
        var rowCount = later ? 33 : 21;
        record[1..header].Fill(NoCarrier);
        for (var row = 0; row < rowCount; row++)
            record[header + row * rowSize] = NoCarrier;
    }

    private static CardDataKind CardKind(byte standard) => standard switch
    {
        >= 1 and <= 9 => CardDataKind.Magnetic,
        >= 10 and <= 16 => CardDataKind.StoredValue,
        _ => CardDataKind.Unknown
    };

    private static NativeTableField CardStandardField(string key, int offset, bool later) =>
        new($"{key}.format", "Card number standard", offset + CardStandardOffset, CardStandard, Choices:
        [new(0, "Unset"), new(1, "Mod 10"), new(2, "ANSI"), new(3, "ABA"), new(4, "CBA"), new(5, "BOC"),
            new(6, "ANSI 59"), new(7, "CCITT"), new(8, "PINOFF (read PIN from stripe)"), new(9, "Hello"), new(10, "Stored-value card"),
            .. (later ? new TableFieldChoice[] { new(11, "Mondex or desk (firmware-specific)"), new(12, "SmartCity GPM416"),
                new(13, "SmartCity PCOS"), new(14, "SmartCity MPCOS"), new(15, "Proton"), new(16, "Visa or CT (firmware-specific)") }
                : [new(11, "Desk terminal")]), new(255, "Unused")],
            Help: "Changing card families clears the stored service-code or stored-value data for this range.");

    private static NativeTableSection[] CardSections(ReadOnlySpan<byte> record)
    {
        var later = record[0] == 134;
        var stride = later ? 36 : 33;
        var sections = new List<NativeTableSection>();
        for (var index = 0; index < (record.Length - 1) / stride; index++)
        {
            var key = $"card-{index}";
            var name = $"Card range {index + 1}";
            var offset = 1 + index * stride;
            var standard = record[offset + CardStandardOffset];
            var kind = CardKind(standard);
            sections.Add(Section(key, name,
                [Digits($"{key}.start", "Lowest six-digit prefix", offset, 3, 6),
                 Digits($"{key}.end", "Highest six-digit prefix", offset + 3, 3, 6),
                 CardStandardField(key, offset, later),
                 Field($"{key}.expiry", "Expiry position after separator", offset + 8),
                 Field($"{key}.initial", "Start-date position after separator", offset + 9),
                 Field($"{key}.discretionary", "Discretionary-data position", offset + 10),
                 Field($"{key}.reference", "Card reference number", offset + 31),
                 CarrierReference($"{key}.carrier", "Carrier reference", offset + 32),
                 Toggle($"{key}.unformattedRequest", "Send unformatted authorization request", offset + 7, 1),
                 Toggle($"{key}.positiveValidation", kind == CardDataKind.StoredValue
                     ? "Accept listed manufacturers only" : "Accept listed service codes only", offset + 7, 2),
                 Toggle($"{key}.hostValidation", "Require host validation", offset + 7, 4),
                 Toggle($"{key}.creditPath", "Use credit-card processing", offset + 7, 8),
                 Toggle($"{key}.delayedAuthorization", "Delay card-specific authorization", offset + 7, 16),
                 Toggle($"{key}.promptPin", "Prompt for PIN", offset + 7, 32),
                 Toggle($"{key}.promptTelcoPin", "Prompt for the carrier PIN", offset + 7, 64),
                 Toggle($"{key}.routeAccs", "Route to calling-card service", offset + 7, 128)]));

            switch (kind)
            {
                case CardDataKind.Magnetic:
                    sections.Add(Section($"{key}-service", $"{name} service codes",
                        Enumerable.Range(0, later ? 5 : 10).Select(code => new NativeTableField($"{key}.serviceCode-{code}",
                            $"Service code {code + 1}", offset + CardDataOffset + code * 2, CardServiceCode, 2,
                            Help: "Use three digits. The first blank ends the list.")).ToArray()));
                    if (later)
                        sections.Add(CardSpillSection(key, name, offset));
                    break;
                case CardDataKind.StoredValue when later:
                    sections.Add(Section($"{key}-stored-value", $"{name} stored-value settings",
                        Field($"{key}.discount", "Embedded discount index", offset + 30, max: 10,
                            help: "Use a one-based MT93 discount number, or zero to disable it."),
                        Toggle($"{key}.reloadable", "Reloadable stored-value card", offset + 33, 2),
                        Toggle($"{key}.summaryRequired", "Require a summary card", offset + 33, 4),
                        Toggle($"{key}.showBalance", "Display remaining balance", offset + 33, 8),
                        Toggle($"{key}.reloadOnly", "Use only for magnetic-card reload", offset + 33, 16),
                        Field($"{key}.reloadScreen", "Reload call-screening entry", offset + 34)));
                    break;
            }
            if (kind == CardDataKind.StoredValue && (later || standard == 10))
                sections.AddRange(SmartCodeSections(key, name, offset, later));
        }
        return sections.ToArray();
    }

    private static NativeTableSection CardSpillSection(string key, string name, int offset) =>
        Section($"{key}-spill", $"{name} data spill",
            [new NativeTableField($"{key}.spillLanguage", "Language digits", offset + 35, KeypadDial),
             Toggle($"{key}.spillCallRecord", "Flag calling-card call record for credit-card reporting", offset + 33, 1),
             Field($"{key}.discount", "Embedded discount index", offset + 30, max: 10,
                 help: "Use a one-based MT93 discount number, or zero to disable it."),
             Bits($"{key}.spillEnd", "Spill terminating key", offset + SpillTerminatorOffset, 15,
                 (0, "None"), (1, "#"), (3, "*")),
             .. Enumerable.Range(0, SpillStepCount).SelectMany(step => new[] {
                 Bits($"{key}.spillStep-{step}.type", $"Step {step + 1} sends", offset + SpillStepsOffset + step, 15,
                     (0, "End sequence"), (1, "Called number"), (2, "Card number"), (3, "Language code"), (4, "PIN"),
                     (5, "Expiry year and month"), (6, "Discretionary data"), (7, "Expiry month and year"), (8, "Terminal ID")),
                 Bits($"{key}.spillStep-{step}.timeout", $"Step {step + 1} bong-tone timeout (seconds)", offset + SpillStepsOffset + step, 240)
             })]);

    internal static NativeTableSection[] PrepareCardChanges(Span<byte> record, IReadOnlyDictionary<string, string> changes)
    {
        var later = record[0] == 134;
        var stride = later ? 36 : 33;
        for (var index = 0; index < (record.Length - 1) / stride; index++)
        {
            var key = $"card-{index}";
            if (!changes.TryGetValue($"{key}.format", out var value) || value == null)
                continue;
            var offset = 1 + index * stride;
            var previous = CardKind(record[offset + CardStandardOffset]);
            CardStandardField(key, offset, later).Write(record, value);
            var standard = record[offset + CardStandardOffset];
            var kind = CardKind(standard);
            if (previous == kind)
                continue;
            var data = record.Slice(offset + CardDataOffset, CardDataLength);
            data.Clear();
            if (kind == CardDataKind.StoredValue && (later || standard == 10))
            {
                data[..6].Fill(0x0F);
                data.Slice(6, 8).Fill(byte.MaxValue);
            }
        }
        return CardSections(record);
    }

    internal static void ValidateCardChanges(ReadOnlySpan<byte> original, ReadOnlySpan<byte> record)
    {
        var later = record[0] == 134;
        var stride = later ? 36 : 33;
        for (var index = 0; index < (record.Length - 1) / stride; index++)
        {
            var offset = 1 + index * stride;
            var standard = record[offset + CardStandardOffset];
            var kind = CardKind(standard);
            if (kind == CardDataKind.StoredValue && (later || standard == 10))
            {
                for (var issuer = 0; issuer < 6; issuer++)
                {
                    var position = offset + CardDataOffset + issuer;
                    var value = record[position];
                    if (value != original[position] && value != 0x0F && (value & 15) >= 8)
                        throw new InvalidOperationException($"Choose a validation key slot 0 through 7 for issuer {issuer + 1} of card range {index + 1}, or select issuer code 0 with a disabled key.");
                }
                // The later value reader indexes a low-nibble entry with the packed byte.
                for (var pair = 0; later && pair < 8; pair++)
                {
                    var position = offset + CardDataOffset + 6 + pair;
                    var value = record[position];
                    if (value != original[position] && (value & 15) != 15 && (value & 0xF0) != 0)
                        throw new InvalidOperationException($"For card range {index + 1}, set value code {pair * 2} to index 0 before enabling value code {pair * 2 + 1}.");
                }
            }
            if (kind != CardDataKind.Magnetic)
                continue;
            var codeCount = later ? 5 : 10;
            var codes = record.Slice(offset + CardDataOffset, codeCount * 2);
            if (!codes.SequenceEqual(original.Slice(offset + CardDataOffset, codes.Length)))
            {
                var blank = false;
                for (var code = 0; code < codeCount; code++)
                {
                    if (codes.Slice(code * 2, 2).IndexOfAnyExcept((byte)0) < 0)
                        blank = true;
                    else if (blank)
                        throw new InvalidOperationException($"Enter the service codes for card range {index + 1} without gaps.");
                }
            }

            if (!later || record.Slice(offset + SpillStepsOffset, SpillStepCount + 1)
                .SequenceEqual(original.Slice(offset + SpillStepsOffset, SpillStepCount + 1)))
                continue;
            var ended = false;
            for (var step = 0; step < SpillStepCount; step++)
            {
                if ((record[offset + SpillStepsOffset + step] & 15) == 0)
                    ended = true;
                else if (ended)
                    throw new InvalidOperationException($"Place active data spill steps before End sequence for card range {index + 1}.");
            }
            if (!ended && (record[offset + SpillTerminatorOffset] & 15) != 0)
                throw new InvalidOperationException($"Keep an End sequence step when card range {index + 1} uses a terminating key.");
        }
    }
}
