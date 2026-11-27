using System.Buffers.Binary;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CommonControlTransitionTests
{
    [Fact]
    public void Advertising_retains_target_spares_and_dormant_text_and_copies_only_recognized_controlled_slots()
    {
        var sourceSlots = Enumerable.Repeat(new AdvertisingSlot("",0,0,false,true),20).ToArray();
        var targetSlots = sourceSlots.ToArray();
        sourceSlots[0] = new("New message",25,2,true,false,0x33,0x10);
        targetSlots[0] = new("Old message",12,1,false,false,0xA5,0xB0);
        targetSlots[1] = new("Old second message",30,0,false,false,0x77,0x80);
        sourceSlots[2] = targetSlots[2] = new("Same text",10,0,false,false);
        var source = AdvertisingTableWire.Encode(sourceSlots); var target = AdvertisingTableWire.Encode(targetSlots);
        target[1+2*24+3] = 0x5A; target[1+3*24+5] = 0xA5;
        var result = CommonControlTransitionWire.Project(29,source,target,["text-advertising"]);
        Assert.Empty(result.Findings);
        var expected = target.ToArray();
        source.AsSpan(1,2).CopyTo(expected.AsSpan(1,2)); expected[3] = 0xB0 | 0x42;
        source.AsSpan(5,20).CopyTo(expected.AsSpan(5,20));
        expected[25] = 0; expected[26] = 0; expected[29] = 0;
        Assert.Equal(expected,result.Payload); Assert.Equal(target.Zip(expected).Count(pair => pair.First != pair.Second),result.ChangedBytes);
        Assert.Equal(result.Payload,CommonControlTransitionWire.Project(29,source,result.Payload,["text-advertising"]).Payload);
        var unknown = source.ToArray(); unknown[6] = 0x80;
        var blocked = CommonControlTransitionWire.Project(29,unknown,target,["text-advertising"]);
        Assert.Contains(blocked.Findings,row => row.Blocking); Assert.Equal(target[6],blocked.Payload[6]);
    }

    [Fact]
    public void Interface_and_communications_groups_retain_independent_controls_and_reserved_timing()
    {
        var source = Record(30,68); var target = Record(30,68);
        Word(source,1,123); Word(source,13,456); Word(source,62,789); Word(target,13,77); Word(target,62,88);
        var result = CommonControlTransitionWire.Project(30,source,target,["ui-prompts"]);
        var expected = target.ToArray(); Word(expected,1,123);
        Assert.Equal(expected,result.Payload); Assert.Empty(result.Findings);
        source = Record(32,33); target = Record(32,33);
        Word(source,1,5); Word(source,3,10); source[21] = 4; source[22] = 1; source[24] = 1;
        source.AsSpan(9,4).Fill(0x11); target.AsSpan(9,4).Fill(0xA5);
        result = CommonControlTransitionWire.Project(32,source,target,["communications"]);
        expected = source.ToArray(); target.AsSpan(9,4).CopyTo(expected.AsSpan(9,4));
        Assert.Equal(expected,result.Payload); Assert.DoesNotContain(result.Findings,row => row.Blocking);
        Assert.DoesNotContain(result.Groups.Single().Fields,row => row.Key is "spareStart" or "spareInterval");
        Word(source,1,ushort.MaxValue);
        var blocked = CommonControlTransitionWire.Project(32,source,target,["communications"]);
        Assert.Contains(blocked.Findings,row => row.Blocking);
    }

    [Fact]
    public void Collection_policy_keeps_later_carrier_mode_until_explicitly_mapped()
    {
        var source = Record(34,19); var target = Record(34,19);
        source[1] = 1; source[3] = 1; source[13] = 0xA3; source[17] = 1;
        target[13] = 0xE2;
        var selected = CommonControlTransitionWire.Project(34,source,target,["call-collection"]);
        Assert.Equal(0xE3,selected.Payload[13]); Assert.DoesNotContain(selected.Findings,row => row.Blocking);
        var both = CommonControlTransitionWire.Project(34,source,target,["call-collection","carrier-collection"]);
        Assert.Equal(0xC3,both.Payload[13]); Assert.DoesNotContain(both.Findings,row => row.Blocking);
        Assert.Equal("Combined carrier summary",both.Groups.Single(group => group.Group.Id == "carrier-collection").Fields.Single().Target);
    }

    [Fact]
    public void Routine_call_in_carries_only_time_and_repeat_policy_not_candidate_date_or_clock_adjustment()
    {
        var source = Record(35,21); var target = Record(35,21);
        source[4] = 12; source[5] = 30; source[6] = 25; source[7] = 2; source[8] = 3; source[9] = 4; source[10] = 5; source[11] = 6; source[12] = 7;
        target.AsSpan(1,3).Fill(0xA5); target.AsSpan(13,8).Fill(0x5A);
        var result = CommonControlTransitionWire.Project(35,source,target,["routine-call-in"]);
        var expected = target.ToArray(); source.AsSpan(4,9).CopyTo(expected.AsSpan(4,9));
        Assert.Equal(expected,result.Payload); Assert.Empty(result.Findings);
        source[4] = 25;
        Assert.Contains(CommonControlTransitionWire.Project(35,source,target,["routine-call-in"]).Findings,row => row.Blocking);
    }

    [Fact]
    public void Coin_payment_hardware_and_reporting_are_independent_and_invalid_combinations_block()
    {
        var source = Record(50,105); var target = Record(50,105);
        Word(source,1,25); Word(source,33,3); source[65] = 0x47;
        Word(source,81,100); Word(source,83,10); Word(source,85,80); Word(source,91,8);
        target[65] = 0xA0; Word(target,33,9); Word(target,81,200); Word(target,83,20);
        target.AsSpan(97,8).Fill(0x5A);
        var invalid = CommonControlTransitionWire.Project(50,source,target,["coin-denominations"]);
        Assert.Contains(invalid.Findings,row => row.Blocking); Assert.Equal(0xA2,invalid.Payload[65]);
        var both = CommonControlTransitionWire.Project(50,source,target,["coin-denominations","coin-hardware"]);
        Assert.DoesNotContain(both.Findings,row => row.Blocking); Assert.Equal(0xA3,both.Payload[65]);
        Assert.Equal(3,BinaryPrimitives.ReadUInt16LittleEndian(both.Payload.AsSpan(33)));
        Assert.Equal(100,BinaryPrimitives.ReadUInt16LittleEndian(both.Payload.AsSpan(81)));
        Assert.Equal(target.AsSpan(85,12).ToArray(),both.Payload.AsSpan(85,12).ToArray());
        var all = CommonControlTransitionWire.Project(50,source,target,["coin-denominations","coin-hardware","coin-collection"]);
        Assert.Equal(80,BinaryPrimitives.ReadUInt16LittleEndian(all.Payload.AsSpan(85)));
        Assert.Equal(target.AsSpan(97,8).ToArray(),all.Payload.AsSpan(97,8).ToArray());
        Assert.Equal(all.Payload,CommonControlTransitionWire.Project(50,source,all.Payload,["coin-denominations","coin-hardware","coin-collection"]).Payload);
    }

    [Theory]
    [InlineData(29,481)][InlineData(30,68)][InlineData(32,33)][InlineData(34,19)][InlineData(35,21)][InlineData(50,105)]
    public void Preview_is_read_only_and_rejects_wrong_shape_or_group(int number,int length)
    {
        var source = Record(number,length); var target = Record(number,length); target[^1] = 0xA5;
        Assert.Equal(target,CommonControlTransitionWire.Project(number,source,target,[]).Payload);
        Assert.Throws<InvalidOperationException>(() => CommonControlTransitionWire.Project(number,source,target,["unknown"]));
        Assert.Throws<InvalidOperationException>(() => CommonControlTransitionWire.Project(number,source,target[..^1],[]));
    }

    private static byte[] Record(int number,int length) { var result = new byte[length]; result[0] = (byte)number; return result; }
    private static void Word(byte[] data,int offset,ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset,2),value);
}
