using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class ScreeningTransitionWireTests
{
    [Fact]
    public void Preserves_slot_order_common_fields_and_target_spares_and_remaps_exact_tariff()
    {
        var source = Template(92, 3401); var target = Template(92, 3061);
        Rule(source, 1, "800", 15, 0x29, 255, 3);
        Rule(source, 2, "333?", 4, 0x33, 0, 0x12);
        Rule(source, 180, "911", 0, 0x27, 255, 3);
        Rule(target, 3, "202", 7, 0x33, 8, 2);
        Rule(source, 7, "811", 0, 0x29, 255, 3);
        Rule(target, 7, "811", 0, 0x29, 255, 3);
        for (var slot = 1; slot <= 180; slot++)
        {
            var row = At(target, slot); row[12] = 0xA1; row[13] |= 0xA0; row[14..].Fill(0xB2);
        }
        At(target, 7)[13] |= 0x04; // retained restricted-service option on an unchanged row
        At(source, 2)[12] = 0x71; At(source, 2)[13] = 0xFE; At(source, 2)[14..].Fill(0x81);
        At(target, 2)[13] |= 1;
        var oldSource = source.ToArray(); var oldTarget = target.ToArray();
        var earlierRates = Template(73, 1192, NativeTableLayouts.MultiPay);
        var laterRates = Template(73, 1192, NativeTableLayouts.Later);
        Tariff(earlierRates, 0); Tariff(laterRates, 9);
        var projected = ScreeningTransitionWire.Project(source, target, earlierRates, laterRates);
        Assert.DoesNotContain(projected.Findings, finding => finding.Blocking);
        Assert.Equal(4, projected.SourceConfiguredSlots);
        Assert.Equal(1, projected.SourceReservedSlots); Assert.Equal(1, projected.RetainedLaterFlagsSlots);
        var reference = Assert.Single(projected.References);
        Assert.Equal("tariff", reference.Kind); Assert.Equal(0, reference.SourceIndex); Assert.Equal(9, reference.TargetIndex);
        Assert.Equal([2], reference.Slots); Assert.Equal([9], reference.Options);
        var expected = oldTarget.ToArray();
        for (var slot = 1; slot <= 180; slot++)
        {
            var original = At(source, slot); var result = At(expected, slot);
            original[..12].CopyTo(result); result[13] = (byte)((result[13] & 0xFC) | (original[13] & 3));
        }
        At(expected, 2)[2] = 9;
        Assert.Equal(expected, projected.Payload);
        Assert.Equal([1, 2, 3, 180], projected.ChangedPositions); Assert.Equal([3], projected.ClearedTargetPositions);
        Assert.Equal(oldSource, source); Assert.Equal(oldTarget, target);
        var noOp = ScreeningTransitionWire.Project(source, projected.Payload, earlierRates, laterRates);
        Assert.Equal(0, noOp.ChangedSlots); Assert.Equal(projected.Payload, noOp.Payload);
    }

    [Fact]
    public void Refuses_overflow_unrecognized_classifications_and_overwriting_later_flags()
    {
        var source = Template(92, 3401); var target = Template(92, 3061);
        Rule(source, 181, "333", 7, 0x33, 1, 2);
        At(source, 200)[16] = 1; // an otherwise vacant reserved tail cannot silently disappear
        Rule(source, 1, "800", 15, 0x29, 1, 1); // earlier free route, non-FF rate
        Rule(source, 2, "202", 7, 0xD3, 255, 2); // unqualified upper payment bits
        Rule(target, 2, "212", 7, 0x33, 255, 2); At(target, 2)[13] = 0x40;
        At(source, 3)[3] = 16;
        var result = ScreeningTransitionWire.Project(source, target);
        Assert.All(new[] { 1, 2, 3, 181, 200 }, slot => Assert.Contains(result.Findings, f => f.Blocking && f.Slot == slot));
        Assert.Contains(result.Findings, f => f.Blocking && f.Slot == 2 && f.Message.Contains("later-only"));
        Assert.Throws<InvalidOperationException>(() => ScreeningTransitionWire.Project(target, source));
    }

    [Fact]
    public void Requires_review_for_ambiguous_tariffs_and_carriers_and_rejects_invalid_mapping()
    {
        var source = Template(92, 3401); var target = Template(92, 3061);
        Rule(source, 3, "333", 7, 0x33, 5, 2);
        Rule(source, 4, "555", 7, 0x29, 17, 0x81);
        Rule(source, 5, "556", 7, 0x29, 0, 0x81); // special local/no carrier reference
        var from = Template(73, 1192, NativeTableLayouts.MultiPay); var to = Template(73, 1192, NativeTableLayouts.Later);
        Tariff(from, 5); Tariff(to, 2); Tariff(to, 3);
        var oldCarrier = Template(23, 678); var newCarrier = Template(135, 1109);
        oldCarrier[4] = 17; newCarrier[10] = 21; newCarrier[10 + 33] = 22; newCarrier[10 + 66] = 22;
        var review = ScreeningTransitionWire.Project(source, target, from, to, oldCarrier, newCarrier);
        Assert.Equal(2, review.References.Length);
        var rate = Assert.Single(review.References, row => row.Kind == "tariff");
        Assert.Equal([2, 3], rate.Options); Assert.Null(rate.TargetIndex);
        var carrier = Assert.Single(review.References, row => row.Kind == "carrier");
        Assert.Equal([21], carrier.Options); Assert.Null(carrier.TargetIndex);
        Assert.Contains(review.Findings, row => row.Blocking);
        var selected = ScreeningTransitionWire.Project(source, target, from, to, oldCarrier, newCarrier,
            new Dictionary<int, int> { [5] = 3 }, new Dictionary<int, int> { [17] = 21 });
        Assert.DoesNotContain(selected.Findings, row => row.Blocking);
        Assert.Equal(3, At(selected.Payload, 3)[2]); Assert.Equal(21, At(selected.Payload, 4)[2]);
        Assert.Equal(0, At(selected.Payload, 5)[2]);
        var invalid = ScreeningTransitionWire.Project(source, target, from, to, oldCarrier, newCarrier,
            new Dictionary<int, int> { [5] = 9, [6] = 2 }, new Dictionary<int, int> { [17] = 22 });
        Assert.Contains(invalid.Findings, row => row.Blocking && row.Message.Contains("not used"));
        Assert.Contains(invalid.Findings, row => row.Blocking && row.Message.Contains("not an available"));
        Assert.Contains(ScreeningTransitionWire.Project(source, target).Findings, row => row.Blocking && row.Message.Contains("no compatible"));
        newCarrier[10] = 17;
        Assert.Null(Assert.Single(ScreeningTransitionWire.Project(source, target, from, to, oldCarrier, newCarrier)
            .References, reference => reference.Kind == "carrier").TargetIndex);
    }

    private static Span<byte> At(byte[] record, int slot) => record.AsSpan(1 + (slot - 1) * 17, 17);
    private static byte[] Template(int number, int length, string? profile = null) => NativeTableLayouts.CreateDraftTemplate(
        NativeTableLayouts.DraftTemplates(number).Single(layout => layout.Length == length && (profile == null || layout.Profile == profile)));
    private static void Tariff(byte[] record, int index) => new byte[] { 2, 0xFF, 0xFF, 25, 0, 0, 0, 0, 0 }.CopyTo(record, 40 + index * 9);
    private static void Rule(byte[] record, int slot, string pattern, byte wildcards, byte classification, byte rate, byte flags)
    {
        var layout = NativeTableLayouts.Candidates(92, record).Single();
        layout.Sections[slot - 1].Fields[0].Write(record, pattern);
        var row = At(record, slot); row[0] = flags; row[1] = classification; row[2] = rate; row[3] = wildcards;
    }
}
