using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class QuickAccessTransitionWireTests
{
    [Fact]
    public void Five_key_source_uses_physical_positions_six_to_ten_and_preserves_candidate_spares()
    {
        var source = Record(); var target = Record();
        source = SourceLayout(source).Apply(source, new Dictionary<string,string>
        {
            ["key-5.number"]="2025550123", ["key-5.upper"]="Synthetic route", ["key-5.lower"]="Choose payment",
            ["key-5.customerReference"]="1234", ["key-5.type"]="3", ["key-5.payment"]="3",
            ["key-5.rate"]="7", ["key-5.answer"]="true", ["key-5.advertising"]="true"
        });
        source[Offset(1)+4]=0x11; // inactive on the five-key layout
        target[Offset(1)+4]=0x22; target[Offset(6)]=0x18;
        target[Offset(6)+55]=0xA0; target[Offset(6)+56]=0x80;
        var oldTariff=Tariffs(); var newTariff=Tariffs(); SetRate(oldTariff,7); SetRate(newTariff,4);
        var result=QuickAccessTransitionWire.Project(source,target,5,10,oldTariff,newTariff);
        Assert.DoesNotContain(result.Findings,f=>f.Blocking);
        Assert.Equal(new[] {6,7,8,9,10},result.SourceActiveSlots); Assert.Equal(new[] {1,2,3,4,5},result.RetainedTargetSlots);
        Assert.Equal(new[] {6},result.ChangedSlots); Assert.Equal(4, Assert.Single(result.References).TargetIndex);
        Assert.Equal(target[..Offset(6)],result.Payload[..Offset(6)]);
        var row=result.Payload.AsSpan(Offset(6),57);
        Assert.Equal(0x1A,row[0]); Assert.Equal(0x33,row[1]); Assert.Equal(4,row[2]);
        Assert.Equal(source.AsSpan(Offset(6)+3,52).ToArray(),row[3..55].ToArray());
        Assert.Equal(0xA0,row[55]); Assert.Equal(0x81,row[56]);
        Assert.Contains(result.Findings,f=>f.Message.Contains("Inactive source"));
    }

    [Fact]
    public void Ten_to_five_blocks_populated_or_reserved_overflow_without_moving_keys()
    {
        var source=Record(); var target=Record();
        source[Offset(1)+4]=0x11; source[Offset(2)+55]=0x80;
        target[Offset(1)+4]=0x22;
        var result=QuickAccessTransitionWire.Project(source,target,10,5);
        Assert.Contains(result.Findings,f=>f.Blocking&&f.Slot==1);
        Assert.Contains(result.Findings,f=>f.Blocking&&f.Slot==2);
        Assert.Equal(target,result.Payload);
        Assert.Throws<InvalidOperationException>(()=>QuickAccessTransitionWire.Project(source,target,7,5));
    }

    [Fact]
    public void Free_feature_group_reference_needs_explicit_unique_carrier_and_preserves_unrelated_keys()
    {
        var source=Record(); var target=Record();
        source=SourceLayout(source).Apply(source,new Dictionary<string,string>
        {
            ["key-5.number"]="2025550123", ["key-5.free"]="true", ["key-5.featureGroupB"]="true",
            ["key-5.type"]="9", ["key-5.payment"]="2", ["key-5.rate"]="7"
        });
        var oldCarrier=new byte[678]; oldCarrier[0]=23; oldCarrier[4]=7;
        var newCarrier=new byte[1109]; newCarrier[0]=135; newCarrier[10]=9; newCarrier[43]=10;
        var pending=QuickAccessTransitionWire.Project(source,target,5,5,sourceCarriers:oldCarrier,targetCarriers:newCarrier);
        var reference=Assert.Single(pending.References); Assert.Equal("carrier",reference.Kind);
        Assert.Equal(new[] {9,10},reference.Options); Assert.Null(reference.TargetIndex);
        Assert.Contains(pending.Findings,f=>f.Blocking);
        var confirmed=QuickAccessTransitionWire.Project(source,target,5,5,sourceCarriers:oldCarrier,targetCarriers:newCarrier,
            carrierChoices:new Dictionary<int,int>{{7,10}});
        Assert.DoesNotContain(confirmed.Findings,f=>f.Blocking); Assert.Equal(10,confirmed.Payload[Offset(6)+2]);
        newCarrier[76]=10;
        var duplicate=QuickAccessTransitionWire.Project(source,target,5,5,sourceCarriers:oldCarrier,targetCarriers:newCarrier,
            carrierChoices:new Dictionary<int,int>{{7,10}});
        Assert.Contains(duplicate.Findings,f=>f.Blocking);
        source[Offset(6)+2]=0;
        var defaultCarrier=QuickAccessTransitionWire.Project(source,target,5,5);
        Assert.Empty(defaultCarrier.References);
        Assert.Contains(defaultCarrier.Findings,f=>!f.Blocking&&f.Slot==6&&f.Message.Contains("default carrier"));
    }

    [Fact]
    public void Changed_candidate_later_options_and_source_only_application_never_silently_translate()
    {
        var source=Record(); var target=Record();
        source[Offset(6)+55]=8; source[Offset(6)+56]=0x40;
        target[Offset(6)+55]=1; target[Offset(6)+4]=0x11;
        var result=QuickAccessTransitionWire.Project(source,target,5,5);
        Assert.Contains(result.Findings,f=>f.Blocking&&f.Slot==6&&f.Message.Contains("change-card"));
        Assert.Contains(result.Findings,f=>f.Blocking&&f.Slot==6&&f.Message.Contains("later-only"));
        Assert.Equal(1,result.Payload[Offset(6)+55]);
        Assert.Equal(0,result.Payload[Offset(6)+56]);
        Assert.Equal(new byte[8],result.Payload[(Offset(6)+3)..(Offset(6)+11)]);
    }

    private static byte[] Record() { var record=new byte[571]; record[0]=55; return record; }
    private static NativeTableLayout SourceLayout(byte[] record)=>NativeTableLayouts.Find(55,record,NativeTableLayouts.MultiPay)!;
    private static int Offset(int slot)=>1+(slot-1)*57;
    private static byte[] Tariffs()
    {
        var record=new byte[1192]; record[0]=73;
        for(var i=0;i<128;i++)record[40+i*9]=3;
        return record;
    }
    private static void SetRate(byte[] record,int index)=>new byte[] {2,0xff,0xff,25,0,0,0,0,0}.CopyTo(record,40+index*9);
}
