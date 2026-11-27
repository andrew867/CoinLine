using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CardRouteReadinessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Card_swipe_acceptance_does_not_establish_carrier_or_screening_or_host_rate_readiness(bool later)
    {
        var cardType = later ? 134 : 22;
        var carrierType = later ? 135 : 23;
        var cards = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(cardType)));
        cards[1] = cards[4] = 0x12; cards[7] = 1; cards[8] = 0x0C; cards[33] = 0;
        var carrier = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(carrierType)));
        var header = later ? 10 : 4;
        var stride = later ? 33 : 32;
        carrier[header] = 0; carrier[header + 1] = carrier[header + 2] = 0xFF; carrier[header + 3] = 1;
        carrier[header + stride] = 5;
        carrier[3] = 5;
        if (later) carrier[9] = 0;
        var screen = new byte[later ? 3061 : 3401]; screen[0] = 92;
        screen[1] = 0x42; screen[2] = 0x35; screen[3] = 1; screen[5] = 0x12;
        var rates = new byte[1192]; rates[0] = 73; rates[49] = 7;
        var plan = new byte[353]; plan[0] = 62; plan[11] = 11;
        var features = new byte[72]; features[0] = 26; features[1] = 2;
        var records = new Dictionary<int, byte[]> { [26] = features, [cardType] = cards, [carrierType] = carrier,
            [92] = screen, [73] = rates, [62] = plan };
        var findings = NativeTableSetReview.Build(records).Findings;
        Assert.Contains(findings, row => row.TableNumber == carrierType && row.Message.Contains("does not accept host-validated card range(s) 1"));
        Assert.Contains(findings, row => row.TableNumber == carrierType && row.Message.Contains("nonzero carrier-code bytes"));
        Assert.Contains(findings, row => row.TableNumber == 92 && row.Message.Contains("Every included explicit paid screening rule"));
        Assert.Contains(findings, row => row.TableNumber == 62 && row.Message.Contains("denies a found inter-area rate"));

        carrier[header + 1] = carrier[header + 2] = 0;
        carrier[header + stride + 3] = 1;
        screen[1] = 2; screen[2] = 0x45;
        plan[11] = 0x45;
        Assert.Empty(NativeTableSetReview.Build(records).Findings);

        screen[1] = 0x42;
        Assert.Contains(NativeTableSetReview.Build(records).Findings, row => row.Message.Contains("selects card payment but also restricts the route to coins"));
        screen[1] = 2; plan[11] = 0x25;
        Assert.Contains(NativeTableSetReview.Build(records).Findings, row => row.Message.Contains("selects no payment for a found rate"));
        plan[11] = 0x45;
        records.Remove(carrierType);
        Assert.Contains(NativeTableSetReview.Build(records).Findings, row => row.TableNumber == cardType && row.Message.Contains($"Include MT{carrierType}"));
    }

    [Fact]
    public void Intentional_coin_only_screening_without_host_credit_does_not_trigger_card_advisory()
    {
        var records = new Dictionary<int, byte[]> { [92] = new byte[3401], [26] = new byte[72] };
        records[26][0] = 26; records[26][1] = 3;
        var screen = records[92]; screen[0] = 92; screen[1] = 0x42; screen[2] = 0x35; screen[3] = 1; screen[5] = 0x12;
        Assert.Empty(NativeTableSetReview.Build(records).Findings);
    }
}
