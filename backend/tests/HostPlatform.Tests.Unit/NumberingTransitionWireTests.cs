using System.Text.Json;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class NumberingTransitionWireTests
{
    [Fact]
    public void Carries_exact_linked_graphs_and_ordinary_masks_without_copying_currency_or_site_bytes()
    {
        var (source,target) = Fixture(); var oldSource = source.ToArray(); var oldTarget = target.ToArray();
        var result = NumberingTransitionWire.Project(source,target);
        Assert.DoesNotContain(result.Findings,row => row.Blocking);
        var expected = target.ToArray(); expected[1] = 0xF3; expected[2] = 10; expected[3] = 12;
        source.AsSpan(4,4).CopyTo(expected.AsSpan(4,4)); expected[8] = 0x33; source.AsSpan(14,5).CopyTo(expected.AsSpan(14,5)); expected[32] = 0xA1;
        expected.AsSpan(33,14).Clear(); source.AsSpan(33,8).CopyTo(expected.AsSpan(33,8)); source.AsSpan(45,8).CopyTo(expected.AsSpan(45,8));
        expected.AsSpan(213,6).Clear(); expected.AsSpan(345,8).Clear(); source.AsSpan(213,6).CopyTo(expected.AsSpan(213,6)); source.AsSpan(225,6).CopyTo(expected.AsSpan(225,6));
        Assert.Equal(expected,result.Payload); Assert.Equal(oldSource,source); Assert.Equal(oldTarget,target);
        Assert.Equal(target[19..32],result.Payload[19..32]); Assert.Equal(target[70],result.Payload[70]); Assert.Equal(target[240],result.Payload[240]);
        Assert.All(result.Payload[345..353],value => Assert.Equal(0,value));
        Assert.Equal(JsonSerializer.Serialize(NumberingPlanWire.Read(source)),JsonSerializer.Serialize(NumberingPlanWire.Read(result.Payload,later:true)));
        Assert.Equal(new[] { "dial","format" },result.Graphs.Select(row => row.Section));
        var format = result.Graphs[1]; Assert.Equal(132,format.SourceCapacity); Assert.Equal(140,format.TargetCapacity);
        Assert.Equal(2,format.SourceRules); Assert.Equal(2,format.CandidateRules); Assert.Equal(2,format.ProposedRules);
        Assert.Equal(1,format.SourceUnassignedBytes); Assert.Equal(1,format.TargetUnassignedBytes);
        Assert.Contains(result.Fields,row => row.Key == "currencySymbol" && row.Action == "source-only");
        Assert.Contains(result.Fields,row => row.Key == "storedValueDiscount" && row.Action == "retained-target");
        var desk = Assert.Single(result.Fields,row => row.Key == "keyCardNumber"); Assert.True(desk.Protected); Assert.Null(desk.SourceValue); Assert.Null(desk.TargetBefore); Assert.Null(desk.TargetAfter);
        var repeat = NumberingTransitionWire.Project(source,result.Payload); Assert.Equal(0,repeat.ChangedBytes); Assert.Equal(result.Payload,repeat.Payload);
    }

    [Fact]
    public void Independently_retains_candidate_header_and_graphs_with_explicit_protected_desk_copy()
    {
        var (source,target) = Fixture();
        var result = NumberingTransitionWire.Project(source,target,carryHeader:false,carryDial:false,carryFormat:false,carryDeskCredential:true);
        Assert.DoesNotContain(result.Findings,row => row.Blocking);
        var expected = target.ToArray(); source.AsSpan(21,5).CopyTo(expected.AsSpan(21,5)); Assert.Equal(expected,result.Payload);
        Assert.All(result.Graphs,row => Assert.Equal("retained-target",row.Action));
        Assert.Contains(result.Fields,row => row.Key == "keyCardNumber" && row.Action == "changed" && row.SourceValue == null);
        source[21] = 0xFA;
        Assert.Contains(NumberingTransitionWire.Project(source,target,carryDeskCredential:true).Findings,row => row.Blocking && row.Message.Contains("key-card"));
        Assert.DoesNotContain(NumberingTransitionWire.Project(source,target).Findings,row => row.Blocking);
        source[2] = byte.MaxValue;
        Assert.Contains(NumberingTransitionWire.Project(source,target).Findings,row => row.Blocking && row.Message.Contains("terminating key"));
        Assert.DoesNotContain(NumberingTransitionWire.Project(source,target,carryHeader:false).Findings,row => row.Blocking);
    }

    [Fact]
    public void Refuses_unknown_candidate_overlap_unrecognized_graph_and_empty_source_replacing_an_active_plan()
    {
        var (source,target) = Fixture(); target[48] = 0xA5;
        var overlap = NumberingTransitionWire.Project(source,target);
        Assert.Contains(overlap.Findings,row => row.Blocking && row.Message.Contains("unassigned candidate bytes"));
        Assert.Equal(target[33..213],overlap.Payload[33..213]);
        target[48] = 0; source[38] = 0; // invalid earlier root pattern
        var unrecognized = NumberingTransitionWire.Project(source,target);
        Assert.Contains(unrecognized.Findings,row => row.Blocking && row.Message.Contains("not fully recognized"));
        Assert.DoesNotContain(NumberingTransitionWire.Project(source,target,carryDial:false).Findings,row => row.Blocking);
        target[38] = 0;
        Assert.Contains(NumberingTransitionWire.Project(source,target,carryDial:false).Findings,row => row.Blocking && row.Message.Contains("candidate"));
        (source,target) = Fixture(); source.AsSpan(33,180).Clear();
        Assert.Contains(NumberingTransitionWire.Project(source,target).Findings,row => row.Blocking && row.Message.Contains("no dialing rules"));
        Assert.DoesNotContain(NumberingTransitionWire.Project(source,target,carryDial:false).Findings,row => row.Blocking);
    }

    [Fact]
    public void Preserves_imported_match_link_cycle_and_later_unassigned_tail_but_rejects_fallback_cycle()
    {
        var (source,target) = Fixture(); source.AsSpan(33,180).Clear();
        new byte[] { 7,0,0,48,3,1,2 }.CopyTo(source,33); new byte[] { 0,7,0,48,4,1,3 }.CopyTo(source,40);
        target.AsSpan(213,140).Clear(); new byte[] { 0,0,4,0,0x51,0 }.CopyTo(target,213); target[352] = 0xA5;
        var result = NumberingTransitionWire.Project(source,target);
        Assert.DoesNotContain(result.Findings,row => row.Blocking); Assert.Contains(result.Findings,row => !row.Blocking && row.Message.Contains("match link"));
        Assert.Equal(source[33..47],result.Payload[33..47]); Assert.Equal(0xA5,result.Payload[352]);
        source[40] = 7; source[41] = 0;
        Assert.Contains(NumberingTransitionWire.Project(source,target).Findings,row => row.Blocking && row.Message.Contains("circular fallback"));
    }

    private static (byte[] Source,byte[] Target) Fixture()
    {
        var source = new byte[353]; source[0] = 62; var target = new byte[353]; target[0] = 62;
        source[1] = 0xA3; target[1] = 0xF0; source[2] = 10; source[3] = 12; source[4] = 40; source[6] = 50;
        for (var index = 8; index <= 13; index++) source[index] = target[index] = 0x29;
        source[8] = 0xB3; target[8] = 0x44; source[14] = 1; source[16] = 1; source[17] = 1; source[18] = 1;
        source.AsSpan(19,13).Fill(0xA5); target.AsSpan(19,13).Fill(0x5A);
        var earlier = NativeTableLayouts.Find(62,source,NativeTableLayouts.MultiPay)!; var later = NativeTableLayouts.Find(62,target,NativeTableLayouts.Later)!;
        source = earlier.Apply(source,new Dictionary<string,string> { ["keyCardNumber"] = string.Concat(Enumerable.Range(0,10)) });
        target = later.Apply(target,new Dictionary<string,string> { ["keyCardNumber"] = string.Concat(Enumerable.Range(0,10).Reverse()) });
        source[32] = 0x81; target[32] = 0xA0;
        new byte[] { 0,0,16,16,0x24,1,2,12 }.CopyTo(source,33); new byte[] { 0,0,16,16,0x25,1,3,0 }.CopyTo(source,45); source[43] = 0xB1;
        new byte[] { 7,0,0,48,3,1,2 }.CopyTo(target,33); new byte[] { 0,0,0,48,4,1,3 }.CopyTo(target,40); target[70] = 0xA5;
        new byte[] { 12,0,4,0,0x51,0 }.CopyTo(source,213); new byte[] { 0,0,4,0,0x91,0 }.CopyTo(source,225); source[223] = 0xB1;
        source[345] = (byte)'$'; source[349] = 6; source[350] = 5; source[351] = 2; source[352] = 1;
        new byte[] { 132,0,4,0,0x51,0 }.CopyTo(target,213); new byte[] { 0,0,4,0,0x91,0x51,0x91,0 }.CopyTo(target,345); target[240] = 0xA5;
        return (source,target);
    }
}
