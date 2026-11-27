using HostPlatform.Protocols.Tables;
using System.Buffers.Binary;
using System.Globalization;

namespace HostPlatform.Tests.Unit;

public sealed class EditorCataloguePreservationTests
{
    public static IEnumerable<object[]> Layouts() => NativeTableLayouts.ReferenceLayouts()
        .Select(row => new object[] { row.Number, row.Length, row.Profile });

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_explicit_layout_preserves_imported_bytes_on_unchanged_save(int number, int length, string profile)
    {
        // Synthetic data only: these vectors exercise preservation of unknown,
        // reserved and out-of-range values, not valid device configuration.
        foreach (var seed in new[] { 0, 26, 93 })
        {
            var record = new byte[length];
            if (seed != 0)
                new Random(seed).NextBytes(record);
            record[0] = (byte)number;
            if (number is >= 131 and <= 133)
                for (var screen = 0; screen < 8; screen++)
                    record[8 + 1152 * screen] = new byte[] { 0, (byte)'B', (byte)'T', (byte)'M' }[screen % 4];

            var layout = Assert.IsType<NativeTableLayout>(NativeTableLayouts.Find(number, record, profile));
            Assert.Equal(profile, layout.Profile);
            Assert.Equal(number, layout.Number);
            Assert.Equal(length, layout.Length);
            var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
            Assert.NotEmpty(fields);
            Assert.All(fields, field => Assert.InRange(field.Offset, 1, length - field.Width));
            Assert.All(fields.Where(field => field.IsCredential), field => Assert.Null(field.Read(record)));
            var values = fields.Select(field => (field.Key, Value: field.Read(record)))
                .Where(field => field.Value != null).ToDictionary(field => field.Key, field => field.Value!);

            Assert.Equal(record, layout.Apply(record, values));
            Assert.Equal(record, layout.Apply(record, new Dictionary<string, string>()));
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void A_single_field_edit_preserves_every_unrelated_byte_and_bit(int number, int length, string profile)
    {
        var template = NativeTableLayouts.DraftTemplates(number)
            .SingleOrDefault(row => row.Length == length && row.Profile == profile);
        var record = template == null ? new byte[length] : NativeTableLayouts.CreateDraftTemplate(template);
        record[0] = (byte)number;
        var layout = Assert.IsType<NativeTableLayout>(NativeTableLayouts.Find(number, record, profile));
        var choices = layout.Sections.SelectMany(section => section.Fields)
            .Where(field => !field.IsCredential)
            .OrderByDescending(field => field.IsSpare)
            .Select(field => (Field: field, Value: Alternative(field, record)))
            .Where(row => row.Value != null).ToArray();
        Assert.NotEmpty(choices);
        var (selected, value) = choices[0];
        var updated = layout.Apply(record, new Dictionary<string, string> { [selected.Key] = value! });

        Assert.Equal(record[..selected.Offset], updated[..selected.Offset]);
        Assert.Equal(record[(selected.Offset + selected.Width)..], updated[(selected.Offset + selected.Width)..]);
        Assert.NotEqual(record.AsSpan(selected.Offset, selected.Width).ToArray(), updated.AsSpan(selected.Offset, selected.Width).ToArray());
        if (selected.Encoding is TableFieldEncoding.Flag or TableFieldEncoding.Bits)
            Assert.Equal(0, (record[selected.Offset] ^ updated[selected.Offset]) & ~selected.Mask);
        if (selected.Encoding == TableFieldEncoding.BigEndianBits)
            Assert.Equal(0, (BinaryPrimitives.ReadUInt16BigEndian(record.AsSpan(selected.Offset)) ^
                BinaryPrimitives.ReadUInt16BigEndian(updated.AsSpan(selected.Offset))) & ~selected.Mask);
        var reopened = Assert.IsType<NativeTableLayout>(NativeTableLayouts.Find(number, updated, profile));
        var reopenedField = Assert.Single(reopened.Sections.SelectMany(section => section.Fields), field => field.Key == selected.Key);
        Assert.Equal(value, reopenedField.Read(updated));
        Assert.Equal(updated, reopened.Apply(updated, new Dictionary<string, string> { [selected.Key] = value! }));
    }

    private static string? Alternative(NativeTableField field, byte[] record)
    {
        var current = field.Read(record);
        if (current == null)
            return null;
        if (field.Encoding == TableFieldEncoding.Flag)
            return current == "true" ? "false" : "true";
        if (field.Encoding is TableFieldEncoding.Dial or TableFieldEncoding.KeypadDial)
            return current == "1" ? "2" : "1";
        if (field.Encoding is TableFieldEncoding.DisplayText or TableFieldEncoding.Text)
            return current == "A" ? "B" : "A";
        if (field.Encoding is not (TableFieldEncoding.UInt8 or TableFieldEncoding.Word or TableFieldEncoding.BigEndianWord
            or TableFieldEncoding.SignedWord or TableFieldEncoding.SignedLong or TableFieldEncoding.UnsignedLong
            or TableFieldEncoding.SignedByte or TableFieldEncoding.Bits or TableFieldEncoding.BigEndianBits))
            return null;
        // Skip selectors which reinterpret a record or activate a dependent service.
        if (field.Choices != null)
            return null;
        foreach (var candidate in new long[] { 0, 1, field.LowerBound, field.UpperBound })
            if (candidate >= field.LowerBound && candidate <= field.UpperBound && candidate.ToString(CultureInfo.InvariantCulture) != current)
                return candidate.ToString(CultureInfo.InvariantCulture);
        return null;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(26)]
    [InlineData(93)]
    public void Dedicated_text_advertising_editor_preserves_imported_slots_and_changes_one_spare_byte(int seed)
    {
        var record = new byte[AdvertisingTableWire.RecordLength];
        if (seed != 0)
            new Random(seed).NextBytes(record);
        record[0] = AdvertisingTableWire.MessageType;
        // Include an end marker even when later slots contain retained bytes.
        record[1 + 24 * 3 + 4] = 0;
        var values = AdvertisingTableWire.Read(record);
        Assert.Equal(record, AdvertisingTableWire.Encode(values, record));
        var expected = record.ToArray();
        expected[1 + 24 * 7 + 3] ^= 0x01;
        values[7] = values[7] with { SpareByte = expected[1 + 24 * 7 + 3] };
        Assert.Equal(expected, AdvertisingTableWire.Encode(values, record));
    }
}
