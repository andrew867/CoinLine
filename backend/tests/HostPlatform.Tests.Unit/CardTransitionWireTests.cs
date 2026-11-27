using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CardTransitionWireTests
{
    [Fact]
    public void Places_ranges_in_source_order_and_preserves_later_only_bytes_and_unrelated_entries()
    {
        var f = Fixture();
        var originalSource = f.Source.ToArray(); var originalTarget = f.Target.ToArray();
        var result = f.Project();
        Assert.DoesNotContain(result.Findings, finding => finding.Blocking);
        Assert.Equal([new CardTransitionRow(1, 3, true, true), new CardTransitionRow(3, 5, false, true)], result.Rows);
        Assert.Equal([3, 5], result.ChangedSlots);
        var expected = originalTarget.ToArray();
        Source(f.Source, 1)[..21].CopyTo(Target(expected, 3)); Source(f.Source, 1)[31..33].CopyTo(Target(expected, 3)[31..]);
        Source(f.Source, 3)[..21].CopyTo(Target(expected, 5)); Source(f.Source, 3)[31..33].CopyTo(Target(expected, 5)[31..]);
        Assert.Equal(expected, result.Payload);
        Assert.Equal(originalSource, f.Source); Assert.Equal(originalTarget, f.Target);
        Assert.Equal(originalTarget.AsSpan(1 + 4 * 36 + 21, 10).ToArray(), Target(result.Payload,5).Slice(21,10).ToArray());
        Assert.Equal(originalTarget.AsSpan(1 + 4 * 36 + 33, 3).ToArray(), Target(result.Payload,5)[33..].ToArray());
        var repeat = CardTransitionWire.Project(f.Source, result.Payload, f.SourceCarriers, f.TargetCarriers);
        Assert.Empty(repeat.ChangedSlots); Assert.Equal(expected, repeat.Payload);

        var earlierScreen = Draft(92, 3401); var laterScreen = Draft(92, 3061);
        var carriers = CarrierTransitionWire.Project(f.SourceCarriers, f.TargetCarriers, f.Source, result.Payload,
            earlierScreen, laterScreen, result.Rows.ToDictionary(row => row.SourceSlot, row => row.TargetSlot));
        Assert.DoesNotContain(carriers.Findings, finding => finding.Blocking);
        // Source carrier 7 accepts the first range and rejects the second;
        // unrelated candidate carrier 9 keeps all its original bits.
        Assert.Equal(0x04, Carrier(carriers.Payload, 5)[3] & 0x14);
        Assert.Equal(Carrier(f.TargetCarriers, 9).ToArray(), Carrier(carriers.Payload, 9).ToArray());
        Assert.Equal(0x10, Carrier(carriers.Payload, 1)[3] & 0x14);
    }

    [Fact]
    public void Refuses_service_code_overflow_gaps_noncanonical_encoding_and_other_applications()
    {
        var f = Fixture();
        Source(f.Source, 1)[21] = 0x12; Source(f.Source, 1)[22] = 0x3E;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("five later"));
        Source(f.Source, 1)[21..23].Clear(); Source(f.Source, 1)[13..15].Clear();
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("gap"));
        Source(f.Source, 1)[13] = 0x12; Source(f.Source, 1)[14] = 0x3D;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("encoding"));
        Source(f.Source, 1)[14] = 0x3E; Source(f.Source, 3)[6] = 10;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("application"));
        Source(f.Source, 3)[6] = 1; Source(f.Source, 3)[2] = 0xFA;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("malformed range"));
    }

    [Fact]
    public void Refuses_overlapping_unrelated_ranges_duplicate_identity_order_reversal_and_unsafe_vacancies()
    {
        var f = Fixture();
        SetRange(Target(f.Target, 10), 1, 1, 9);
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("overlaps unrelated"));
        CardMasterWire.Empty(134).CopyTo(Target(f.Target, 10));
        Target(f.Target, 5).CopyTo(Target(f.Target, 10));
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("more than one target"));
        CardMasterWire.Empty(134).CopyTo(Target(f.Target, 10));
        Source(f.Source, 1).CopyTo(Source(f.Source, 4));
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("duplicate range"));
        CardMasterWire.Empty(22).CopyTo(Source(f.Source,4));
        SetRange(Target(f.Target, 6), 1, 1, 7);
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("selection order"));
        CardMasterWire.Empty(134).CopyTo(Target(f.Target, 6));
        Carrier(f.TargetCarriers, 9)[3] |= 0x0C;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("no completely vacant"));
    }

    private sealed record FixtureData(byte[] Source, byte[] Target, byte[] SourceCarriers, byte[] TargetCarriers)
    { public CardTransitionProjection Project() => CardTransitionWire.Project(Source, Target, SourceCarriers, TargetCarriers); }

    private static FixtureData Fixture()
    {
        var source = Draft(22); var target = Draft(134); var sourceCarriers = Draft(23); var targetCarriers = Draft(135);
        var first = Source(source,1); SetRange(first,1,1,7); first[7] = 0x0C; first[8] = 3; first[9] = 4; first[10] = 5; first[31] = 7;
        for (var index = 0; index < 5; index++) { first[11 + index * 2] = 0x12; first[12 + index * 2] = 0x3E; }
        var second = Source(source,3); SetRange(second,2,2,8); second[7] = 4; second[31] = 8;
        var match = Target(target,5); SetRange(match,2,2,8); match[21] = 0xA5; match[29] = 0xB0; match[33] = 0x60; match[34] = 17; match[35] = 0xAB;
        Target(target,1)[35] = 0xA5; // not a completely vacant position
        SetRange(Target(target,9), 8,8,9);
        sourceCarriers[4] = 7; sourceCarriers[7] = 1; sourceCarriers[36] = 8; sourceCarriers[39] = 4;
        Carrier(targetCarriers,5)[0] = 7; Carrier(targetCarriers,5)[3] = 0x10;
        Carrier(targetCarriers,9)[0] = 9; Carrier(targetCarriers,9)[3] = 0x02; // stale acceptance on a vacant position
        return new(source,target,sourceCarriers,targetCarriers);
    }
    private static byte[] Draft(int type, int? length = null) => NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(type).Single(row => length == null || row.Length == length));
    private static Span<byte> Source(byte[] record, int slot) => record.AsSpan(1+(slot-1)*33,33);
    private static Span<byte> Target(byte[] record, int slot) => record.AsSpan(1+(slot-1)*36,36);
    private static Span<byte> Carrier(byte[] record, int slot) => record.AsSpan(10+(slot-1)*33,33);
    private static void SetRange(Span<byte> row, byte first, byte last, byte carrier)
    { row[2] = first; row[5] = last; row[6] = 1; row[32] = carrier; }
}
