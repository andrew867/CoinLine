namespace HostPlatform.Protocols.Tables;

public sealed record QuickAccessTransitionFinding(bool Blocking, int? Slot, string Message);
public sealed record QuickAccessTransitionReference(string Kind, int SourceIndex, int[] Slots, int[] Options, int? TargetIndex);
public sealed record QuickAccessTransitionProjection(byte[] Payload, int[] SourceActiveSlots, int[] TargetActiveSlots,
    int[] ChangedSlots, int[] ClearedSlots, int[] RetainedTargetSlots, QuickAccessTransitionReference[] References,
    QuickAccessTransitionFinding[] Findings);

/// <summary>Carry common earlier quick-access fields into the same physical key positions on a reviewed later layout.</summary>
public static class QuickAccessTransitionWire
{
    private const byte SharedFlags = QuickAccessKeyWire.FreeCall | QuickAccessKeyWire.AnswerSupervision |
        QuickAccessKeyWire.CarrierPrefix | QuickAccessKeyWire.EnableKeypad | QuickAccessKeyWire.Information | QuickAccessKeyWire.FeatureGroupB;

    public static QuickAccessTransitionProjection Project(byte[] source, byte[] target, int sourceKeys, int targetKeys,
        byte[]? sourceTariffs = null, byte[]? targetTariffs = null, byte[]? sourceCarriers = null, byte[]? targetCarriers = null,
        IReadOnlyDictionary<int, int>? tariffChoices = null, IReadOnlyDictionary<int, int>? carrierChoices = null)
    {
        if (source.Length != QuickAccessKeyWire.RecordLength || target.Length != QuickAccessKeyWire.RecordLength ||
            source[0] != QuickAccessKeyWire.MessageType || target[0] != QuickAccessKeyWire.MessageType)
            throw new InvalidOperationException("Choose complete earlier and later MT55 quick-access records.");
        var sourceActive = Active(sourceKeys); var targetActive = Active(targetKeys);
        var sourceLayout = NativeTableLayouts.Find(55, source, NativeTableLayouts.MultiPay)!;
        var output = target.ToArray();
        var findings = new List<QuickAccessTransitionFinding>();
        var references = new Dictionary<(string Kind, int Index), List<int>>();
        foreach (var slot in sourceActive)
        {
            var old = Row(source, slot);
            if (!targetActive.Contains(slot))
            {
                if (old.IndexOfAnyExcept((byte)0) >= 0)
                    Block(slot, "The source key contains data outside the target's physical key positions. Relocate and review it before staging.");
                continue;
            }
            if (!Configured(old)) continue;
            if ((old[55] & 8) != 0 || (old[56] & QuickAccessKeyWire.MessageKey) != 0)
                Block(slot, "This source key uses change-card or messaging behavior with a different later-profile meaning. Review the target key separately.");
            if ((old[1] & 15) is > 11 and < 15 || (old[1] >> 4) > 7)
                Block(slot, "The source key has an unrecognized common call or payment classification.");
            var fields = sourceLayout.Sections[slot - 1].Fields;
            if (fields.Where(f => f.Key.EndsWith(".number") || f.Key.EndsWith(".upper") || f.Key.EndsWith(".lower"))
                .Any(field => field.Read(source) == null))
                Block(slot, "The source key destination or display text has an unrecognized encoding.");
            if ((old[0] & QuickAccessKeyWire.FreeCall) != 0)
            {
                if ((old[0] & QuickAccessKeyWire.FeatureGroupB) != 0 && old[2] is > 0 and < 255) Add("carrier", old[2], slot);
                else if ((old[0] & QuickAccessKeyWire.FeatureGroupB) != 0 && old[2] == 0)
                    findings.Add(new(false, slot, "The free Feature Group B key uses the default carrier reference. Review the prepared target default carrier."));
                else if ((old[0] & QuickAccessKeyWire.FeatureGroupB) == 0 && old[2] is not (0 or 255))
                    Block(slot, "The free key has a noncanonical ordinary rate reference. Review its meaning before carrying it forward.");
            }
            else if ((old[1] & 15) != 11 && old[3..11].IndexOfAnyExcept((byte)0) >= 0)
            {
                if (old[2] >= 128) Block(slot, "The paid destination key has no supported source tariff index.");
                else Add("tariff", old[2], slot);
            }
        }
        var resolved = new List<QuickAccessTransitionReference>();
        foreach (var ((kind, index), slots) in references.OrderBy(pair => pair.Key.Kind).ThenBy(pair => pair.Key.Index))
        {
            var options = kind == "tariff" ? EqualTariffs(sourceTariffs, targetTariffs, index) : CarrierOptions(sourceCarriers, targetCarriers, index);
            var choices = kind == "tariff" ? tariffChoices : carrierChoices;
            int? selected = null;
            if (choices?.TryGetValue(index, out var choice) == true)
            {
                if (options.Contains(choice)) selected = choice;
                else Block(null, $"The requested {kind} mapping for source {index} is not compatible.");
            }
            else if (kind == "tariff" && options.Contains(index)) selected = index;
            else if (kind == "tariff" && options.Length == 1) selected = options[0];
            if (selected == null) Block(null, options.Length == 0
                ? $"Source {kind} {index} has no compatible target reference. Prepare its companion record first."
                : $"Choose the reviewed target {kind} for source {index}.");
            resolved.Add(new(kind,index,slots.ToArray(),options,selected));
        }
        if (tariffChoices?.Keys.Any(index => !references.ContainsKey(("tariff", index))) == true ||
            carrierChoices?.Keys.Any(index => !references.ContainsKey(("carrier", index))) == true)
            Block(null,"The submitted mapping contains a reference that is not used by the active source keys.");
        var mapped = resolved.Where(r => r.TargetIndex.HasValue).SelectMany(r => r.Slots.Select(slot => (slot,r.TargetIndex!.Value)))
            .ToDictionary(row => row.slot,row => row.Value);
        var changed = new List<int>(); var cleared = new List<int>();
        foreach (var slot in sourceActive.Intersect(targetActive))
        {
            var old = Row(source,slot); var before = Row(target,slot);
            var next = output.AsSpan(1+(slot-1)*QuickAccessKeyWire.SlotLength,QuickAccessKeyWire.SlotLength);
            next[0] = (byte)((before[0] & ~SharedFlags) | (old[0] & SharedFlags));
            old[1..55].CopyTo(next[1..55]);
            if (mapped.TryGetValue(slot,out var index)) next[2]=(byte)index;
            // Earlier spare and application bits are not reinterpreted as later features.
            next[56] = (byte)((before[56] & ~QuickAccessKeyWire.Advertising) | (old[56] & QuickAccessKeyWire.Advertising));
            if (!before.SequenceEqual(next))
            {
                changed.Add(slot);
                if ((before[55] & 15) != 0) Block(slot,"The changed candidate key has later-only service, collection or data-port flags. Resolve those in a separate target draft before staging.");
                if (Configured(before) && !Configured(next)) cleared.Add(slot);
            }
        }
        if (sourceActive.Except(targetActive).Any(slot => Row(source,slot).IndexOfAnyExcept((byte)0) >= 0))
            findings.Add(new(false,null,"Keys are never renumbered to fit another physical layout; the source record is retained."));
        if (Enumerable.Range(1,10).Except(sourceActive).Any(slot => Row(source,slot).IndexOfAnyExcept((byte)0) >= 0))
            findings.Add(new(false,null,"Inactive source positions contain data and are not carried into active target keys."));
        if (sourceActive.Any(slot => (Row(source,slot)[0] & ~SharedFlags) != 0 ||
            (Row(source,slot)[55] & QuickAccessKeyWire.VariantSpareFlags(false)) != 0 ||
            (Row(source,slot)[56] & QuickAccessKeyWire.FinalSpareFlags(false)) != 0))
            findings.Add(new(false,null,"Source spare bits are not interpreted as later features; candidate spare bits remain intact."));
        if (resolved.Any(r => r.Kind == "carrier")) findings.Add(new(false,null,"Review target carrier acceptance and routing separately from the key reference."));
        return new(output,sourceActive,targetActive,changed.ToArray(),cleared.ToArray(),Enumerable.Range(1,10).Except(sourceActive.Intersect(targetActive)).ToArray(),resolved.ToArray(),findings.ToArray());

        void Add(string kind,int index,int slot)
        {
            if (!references.TryGetValue((kind,index),out var slots)) references[(kind,index)]=slots=[];
            slots.Add(slot);
        }
        void Block(int? slot,string message) => findings.Add(new(true,slot,message));
    }

    // The documented five-key display layout uses physical positions 6 through 10.
    private static int[] Active(int count) => count switch { 5 => [6,7,8,9,10], 10 => Enumerable.Range(1,10).ToArray(),
        _ => throw new InvalidOperationException("Choose the five- or ten-key physical layout for each profile.") };
    private static ReadOnlySpan<byte> Row(byte[] data,int slot) => data.AsSpan(1+(slot-1)*QuickAccessKeyWire.SlotLength,QuickAccessKeyWire.SlotLength);
    private static bool Configured(ReadOnlySpan<byte> row) => (row[0] & SharedFlags) != 0 || row[1..55].IndexOfAnyExcept((byte)0) >= 0 ||
        (row[55] & 8) != 0 || (row[56] & (QuickAccessKeyWire.Advertising | QuickAccessKeyWire.MessageKey)) != 0;
    private static int[] EqualTariffs(byte[]? source,byte[]? target,int index)
    {
        if (source is not { Length:1192 } || source[0]!=73 || target is not { Length:1192 } || target[0]!=73 || index is <0 or >=128 || source[40+index*9]>8) return [];
        return Enumerable.Range(0,128).Where(i => source.AsSpan(40+index*9,9).SequenceEqual(target.AsSpan(40+i*9,9))).ToArray();
    }
    private static int[] CarrierOptions(byte[]? source,byte[]? target,int index)
    {
        if (source is not { Length:678 } || source[0]!=23 || target is not { Length:1109 } || target[0]!=135 ||
            Enumerable.Range(0,21).Count(i => source[4+i*32]==index)!=1) return [];
        return Enumerable.Range(0,33).Select(i => (int)target[10+i*33]).Where(i => i is >0 and <255)
            .GroupBy(i=>i).Where(g=>g.Count()==1).Select(g=>g.Key).Order().ToArray();
    }
}
