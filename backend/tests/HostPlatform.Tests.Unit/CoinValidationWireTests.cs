using System.Buffers.Binary;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CoinValidationWireTests
{
    [Fact]
    public void Reads_sixteen_configured_slot_values_without_assuming_currency_or_rewriting_flags()
    {
        var record = new byte[CoinValidationWire.RecordLength];
        record[0] = CoinValidationWire.MessageType;
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(1), 25);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(3), -17);
        BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(31), 200);
        record[65] = 0xFF;
        record[66] = 0x82;
        record[67] = 0xC1;
        record[80] = 0x40;
        record[97] = 0xAB;
        var before = record.ToArray();
        var slots = CoinValidationWire.ReadSlots(record)!;
        Assert.Equal(Enumerable.Range(1, 16), slots.Select(slot => slot.Number));
        Assert.Equal(new CoinValidationWire.Slot(1, 25, true, true), slots[0]);
        Assert.Equal(new CoinValidationWire.Slot(2, null, false, true), slots[1]);
        Assert.Equal(new CoinValidationWire.Slot(3, null, true, false), slots[2]);
        Assert.Equal(new CoinValidationWire.Slot(16, 200, false, false), slots[15]);
        Assert.Equal(before, record);

        var layout = NativeTableLayouts.Find(50, record)!;
        var value = layout.Sections.SelectMany(section => section.Fields).Single(field => field.Key == "coin-15.value");
        Assert.Equal("200", value.Read(record));
        var changed = layout.Apply(record, new Dictionary<string, string> { [value.Key] = "100" });
        Assert.Equal(100, CoinValidationWire.ReadSlots(changed)![15].ValueMinorUnits);
        Assert.Equal(before[..31], changed[..31]);
        Assert.Equal(before[33..], changed[33..]);
    }

    [Fact]
    public void Rejects_wrong_type_short_and_long_records()
    {
        var record = new byte[105];
        Assert.Null(CoinValidationWire.ReadSlots(record));
        record[0] = 50;
        Assert.Null(CoinValidationWire.ReadSlots(record[..104]));
        Assert.Null(CoinValidationWire.ReadSlots([.. record, 0]));
        Assert.NotNull(CoinValidationWire.ReadSlots(record));
    }
}
