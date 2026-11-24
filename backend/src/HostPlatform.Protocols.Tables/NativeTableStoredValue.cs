using static HostPlatform.Protocols.Tables.TableFieldEncoding;

namespace HostPlatform.Protocols.Tables;

public static partial class NativeTableLayouts
{
    private const int DiscountCount = 10;
    private const int DiscountMethodOffset = 65;
    private const int DiscountValuesOffset = 81;
    private const int UnitLimitsOffset = 161;

    private static readonly (string Key, string Label)[] DiscountZones =
        [("local", "Local"), ("intra", "Intra-area"), ("inter", "Inter-area"), ("international", "International")];

    private static NativeTableField EncodedKey(string key, string label, int offset) =>
        new(key, label, offset, CredentialHex, 8, Help: "Enter the 16 hexadecimal characters supplied for this firmware slot. Leave blank to keep the stored bytes.");

    private static NativeTableSection[] StoredValueSections(ReadOnlySpan<byte> record, bool later)
    {
        var sections = new List<NativeTableSection>();
        if (later)
        {
            for (var index = 0; index < DiscountCount; index++)
            {
                var fields = new List<NativeTableField>();
                for (var zone = 0; zone < DiscountZones.Length; zone++)
                {
                    var (key, label) = DiscountZones[zone];
                    var prefix = $"discount-{index}.{key}";
                    var method = DiscountMethod(index, zone);
                    var mode = (record[method.Offset] & method.Mask) >> DiscountShift(zone);
                    var offset = DiscountValuesOffset + index * 8 + zone * 2;
                    fields.Add(method);
                    fields.Add(mode switch
                    {
                        0 => Field($"{prefix}Initial", $"{label} initial adjustment (cents)", offset, SignedByte),
                        1 => Field($"{prefix}Initial", $"{label} initial charge (% of normal)", offset),
                        2 => Field($"{prefix}Initial", $"{label} replacement MT73 rate index", offset, max: 127),
                        _ => Field($"{prefix}Initial", $"{label} inactive initial value", offset)
                    });
                    fields.Add(mode switch
                    {
                        0 => Field($"{prefix}Later", $"{label} additional adjustment (cents)", offset + 1, SignedByte),
                        1 => Field($"{prefix}Later", $"{label} additional charge (% of normal)", offset + 1),
                        _ => Field($"{prefix}Later", $"{label} inactive additional value", offset + 1)
                    });
                }
                sections.Add(Section($"discount-{index}", $"Stored-value discount {index + 1}", fields.ToArray()));
            }
        }
        else
        {
            sections.Add(Section("rebates", "Stored-value toll rebates",
                Field("domestic", "Domestic toll rebate (cents)", 191, Word),
                Field("international", "International toll rebate (cents)", 193, Word)));
            sections.Add(Section("face-values", "Large-value card limits", Enumerable.Range(0, 16).Select(index =>
                Field($"face-{index}.limit", $"Maximum face value at index {index}", 129 + index * 2, BigEndianWord)).ToArray()));
        }

        sections.AddRange(Rows("unit", 15, index => $"Stored-value unit index {index}", (index, key) =>
        {
            var offset = UnitLimitsOffset + 2 * index;
            return [new($"{key}.limit", "Maximum card units", offset, BigEndianBits, Mask: 0xFFFC),
                new($"{key}.multiplier", "Unit multiplier", offset, BigEndianBits, Mask: 3,
                    Choices: [new(0, "1"), new(1, "5"), new(2, "10"), new(3, "25")])];
        }));
        sections.Add(Section("stored-value-keys", "Encoded validation keys", Enumerable.Range(0, later ? 8 : 16)
            .Select(index => EncodedKey($"validation-key-{index}", $"Encoded validation key slot {index}", 1 + index * 8)).ToArray()));
        return sections.ToArray();
    }

    private static int DiscountShift(int zone) => 6 - zone * 2;

    private static NativeTableField DiscountMethod(int index, int zone)
    {
        var (key, label) = DiscountZones[zone];
        return new($"discount-{index}.{key}Mode", $"{label} discount method", DiscountMethodOffset + index,
            SmartDiscountMethod, Mask: 3 << DiscountShift(zone),
            Choices: [new(0, "Adjust charge in cents"), new(1, "Percent of normal charge"),
                new(2, "Replacement tariff"), new(3, "Disabled")],
            Help: "Changing the method clears the saved values for this call zone.");
    }

    private static void InitializeStoredValueRecord(Span<byte> record) =>
        record.Slice(DiscountMethodOffset, DiscountCount).Fill(byte.MaxValue);

    internal static IReadOnlyList<NativeTableSection> PrepareStoredValueChanges(Span<byte> record,
        IReadOnlyDictionary<string, string> changes, NativeTableLayout layout)
    {
        if (layout.Profile != Later)
            return layout.Sections;
        for (var index = 0; index < DiscountCount; index++)
        {
            for (var zone = 0; zone < DiscountZones.Length; zone++)
            {
                var field = DiscountMethod(index, zone);
                if (!changes.TryGetValue(field.Key, out var value) || value == null)
                    continue;
                var previous = record[field.Offset];
                field.Write(record, value);
                if (record[field.Offset] != previous)
                    record.Slice(DiscountValuesOffset + index * 8 + zone * 2, 2).Clear();
            }
        }
        return StoredValueSections(record, later: true);
    }

    private static NativeTableSection[] SmartCodeSections(string key, string name, int offset, bool later) =>
    [
        Section($"{key}-smart-issuers", $"{name} issuer validation",
            [.. Enumerable.Range(0, 6).SelectMany(index => new[]
            {
                Bits($"{key}.issuer-{index}", $"Issuer {index + 1} code", offset + CardDataOffset + index, 0xF0),
                Bits($"{key}.issuerKey-{index}", $"Issuer {index + 1} validation key index", offset + CardDataOffset + index, 0x0F,
                    [.. Enumerable.Range(0, 8).Select(slot => (slot, $"Slot {slot}")), (15, "Disabled when issuer code is 0")])
            })]),
        Section($"{key}-smart-values", $"{name} value and manufacturer checks",
            [.. Enumerable.Range(0, 16).Select(index => Bits($"{key}.value-{index}", $"Unit table index for card value code {index}",
                offset + CardDataOffset + 6 + index / 2, index % 2 == 0 ? 0xF0 : 0x0F,
                [.. Enumerable.Range(0, 15).Select(slot => (slot, $"Index {slot}")), (15, "Reject this value code")]) with
                {
                    Help = later && index % 2 == 1 ? "Enabling this odd value code requires index 0 on the preceding even value code." : null
                }),
             .. Enumerable.Range(0, 5).Select(index => Bits($"{key}.manufacturer-{index}", $"Manufacturer code {index + 1}",
                offset + CardDataOffset + 14 + index, 0x1F))])
    ];
}
