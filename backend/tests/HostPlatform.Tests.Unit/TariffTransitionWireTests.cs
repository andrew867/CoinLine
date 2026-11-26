using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TariffTransitionWireTests
{
    [Fact]
    public void Reuses_exact_tuples_and_allocates_only_unreferenced_empty_rows_preserving_header_and_every_other_byte()
    {
        var source = Tariffs(later: false, inactive: 4); var target = Tariffs(later: true, inactive: 4);
        var fixedRate = new byte[] { 2, 0xFF, 0xFF, 25, 0, 0, 0, 0, 0 };
        var measuredRate = new byte[] { 1, 60, 0, 25, 0, 60, 0, 10, 0 };
        var hostRate = new byte[] { 8, 0, 0, 0, 0, 0, 0, 0, 0 };
        fixedRate.CopyTo(source, Offset(0)); fixedRate.CopyTo(source, Offset(5));
        measuredRate.CopyTo(source, Offset(1)); hostRate.CopyTo(source, Offset(2));
        hostRate.CopyTo(target, Offset(3));
        target[Offset(4)] = 6; // active target-only rate
        target[Offset(9) + 3] = 0xA5; // an invalid but noncanonical tuple is not available
        target.AsSpan(1, 39).Fill(0xC3);
        var originalSource = source.ToArray(); var originalTarget = target.ToArray();
        var records = new Dictionary<int, byte[]> { [73] = target, [92] = Template(92, 3061), [55] = Template(55, 571, NativeTableLayouts.Later),
            [93] = Template(93, 225, NativeTableLayouts.Later), [150] = Template(150, 401), [151] = Template(151, 604),
            [136] = Template(136, 203) };
        records[92][3] = 5; // retain even a blanked row's numeric rate field
        records[55][3] = 6;
        records[93] = NativeTableLayouts.Find(93, records[93], NativeTableLayouts.Later)!.Apply(records[93],
            new Dictionary<string, string> { ["discount-0.localMode"] = "2", ["discount-0.localInitial"] = "7" });
        records[150][1] = 0x20; // offset 14 + 2 = 16
        records[151][2] = 2; // retained fallback index 30
        foreach (var (number, raw) in GraphicalStarterWire.Create(enableSampleMenu: true)) records[number] = raw;
        var graph = NativeTableLayouts.Find(131, records[131], NativeTableLayouts.Later)!;
        records[131] = graph.Apply(records[131], new Dictionary<string, string> { ["button-0.rate"] = "8" });
        var result = TariffTransitionWire.Project(source, target, records);
        Assert.DoesNotContain(result.Findings, finding => finding.Blocking);
        Assert.Equal(4, result.Groups.Length);
        var fixedGroup = Assert.Single(result.Groups, group => group.SourceIndex == 0);
        Assert.Equal([0, 5], fixedGroup.SourceIndexes); Assert.Equal(10, fixedGroup.TargetIndex);
        Assert.Equal(-1, fixedGroup.InitialPeriod); Assert.Equal(25, fixedGroup.InitialCharge);
        var measured = Assert.Single(result.Groups, group => group.SourceIndex == 1);
        Assert.Equal(11, measured.TargetIndex); Assert.Equal(10, measured.AdditionalCharge);
        Assert.Equal(3, Assert.Single(result.Groups, group => group.SourceIndex == 2).TargetIndex);
        Assert.Equal([10, 11], result.ChangedPositions);
        Assert.Equal(124, Assert.Single(result.Groups, group => group.Classification == 4).SourceIndexes.Length);
        var protectedRates = result.CandidateReview.ProtectedPositions.ToDictionary(position => position.Index, position => position.RecordNumbers);
        Assert.Contains(92, protectedRates[5]); Assert.Contains(55, protectedRates[6]); Assert.Contains(93, protectedRates[7]);
        Assert.Contains(131, protectedRates[8]); Assert.Contains(150, protectedRates[16]); Assert.Contains(151, protectedRates[30]);
        Assert.All(new[] { 0, 1, 2, 3 }, index => Assert.Contains(index, protectedRates.Keys));
        Assert.DoesNotContain(16, fixedGroup.TargetOptions); Assert.DoesNotContain(30, fixedGroup.TargetOptions);
        var expected = target.ToArray(); fixedRate.CopyTo(expected, Offset(10)); measuredRate.CopyTo(expected, Offset(11));
        Assert.Equal(expected, result.Payload); Assert.Equal(originalSource, source); Assert.Equal(originalTarget, target);
        Assert.Equal(originalTarget[..40], result.Payload[..40]);
        var noOp = TariffTransitionWire.Project(source, result.Payload, records);
        Assert.Empty(noOp.ChangedPositions); Assert.All(noOp.Groups, group => Assert.Equal("reuse", group.Action));
    }

    [Fact]
    public void Explicit_allocations_are_reserved_before_suggestions_and_protected_or_duplicate_choices_are_rejected()
    {
        var source = Tariffs(false, 4); var target = Tariffs(true, 4);
        source[Offset(0)] = 2; source[Offset(0) + 3] = 25; source[Offset(1)] = 1; source[Offset(1) + 3] = 5;
        var screening = Template(92, 3061); screening[3] = 7;
        var records = new Dictionary<int, byte[]> { [73] = target, [92] = screening };
        var proposed = TariffTransitionWire.Project(source, target, records, new Dictionary<int, int> { [1] = 4 });
        Assert.DoesNotContain(proposed.Findings, row => row.Blocking);
        Assert.Equal(5, Assert.Single(proposed.Groups, group => group.SourceIndex == 0).TargetIndex);
        Assert.Equal(4, Assert.Single(proposed.Groups, group => group.SourceIndex == 1).TargetIndex);
        foreach (var choices in new[] { new Dictionary<int, int> { [0] = 7 }, new Dictionary<int, int> { [0] = 1 },
                     new Dictionary<int, int> { [0] = 4, [1] = 4 }, new Dictionary<int, int> { [9] = 9 } })
            Assert.Contains(TariffTransitionWire.Project(source, target, records, choices).Findings, row => row.Blocking);
    }

    [Fact]
    public void Unknown_target_records_block_allocation_but_not_exact_reuse_and_noncommon_source_classes_are_not_guessed()
    {
        var source = Tariffs(false, 4); var target = Tariffs(true, 4);
        source[Offset(0)] = 2; source[Offset(0) + 3] = 25;
        var records = new Dictionary<int, byte[]> { [73] = target, [201] = [201, 0, 0] };
        var blocked = TariffTransitionWire.Project(source, target, records);
        Assert.Equal([201], blocked.CandidateReview.UnreviewedRecords); Assert.Empty(blocked.ChangedPositions);
        Assert.Contains(blocked.Findings, f => f.Blocking && f.SourceIndex == 0);
        source.AsSpan(Offset(0), 9).CopyTo(target.AsSpan(Offset(5), 9));
        var reused = TariffTransitionWire.Project(source, target, records);
        Assert.DoesNotContain(reused.Findings, f => f.Blocking); Assert.Empty(reused.ChangedPositions);
        source[Offset(0)] = 12;
        Assert.Contains(TariffTransitionWire.Project(source, target, records).Findings, f => f.Blocking && f.SourceIndex == 0);
        Assert.Throws<InvalidOperationException>(() => TariffTransitionWire.Project(new byte[100], target, records));
    }

    private static int Offset(int index) => 40 + index * 9;
    private static byte[] Tariffs(bool later, byte inactive)
    {
        var bytes = Template(73, 1192, later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay);
        for (var index = 0; index < 128; index++) bytes[Offset(index)] = inactive;
        return bytes;
    }
    private static byte[] Template(int number, int length, string? profile = null) => NativeTableLayouts.CreateDraftTemplate(
        NativeTableLayouts.DraftTemplates(number).Single(layout => layout.Length == length && (profile == null || layout.Profile == profile)));
}
