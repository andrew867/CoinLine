using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class RoutineCallInRandomizationTests
{
    private static readonly Guid Seed = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Preview_is_stable_within_inclusive_source_bound_and_changes_only_time_bytes()
    {
        var original = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(35).Single());
        original[1] = 0xA5;
        original[4] = 6; original[5] = 30; original[6] = 17;
        original[10] = 12; original[12] = 41; original[20] = 0x81;
        var source = Set(original);
        var plan = RoutineCallInRandomizationPlan.Create(source, new("source-time", Seed: Seed));
        var same = RoutineCallInRandomizationPlan.Create(source, new("source-time", Seed: Seed));
        Assert.Equal(TimeOnly.MinValue, plan.Start);
        Assert.Equal(new TimeOnly(6, 30, 17), plan.End);
        var times = Enumerable.Range(1, 25).Select(n => new Guid(n, 0, 0, new byte[8])).Select(phone =>
        {
            var time = plan.TimeFor(phone);
            Assert.Equal(time, same.TimeFor(phone));
            Assert.InRange(TimeOnly.Parse(time), plan.Start, plan.End);
            var version = plan.SourceFor(source, time).Versions.Single();
            var actual = TableDistributionService.GetEffectivePayload(version);
            Assert.Equal(original[..4], actual[..4]);
            Assert.Equal(original[7..], actual[7..]);
            Assert.Equal(time, NativeTableLayouts.Find(35, actual)!.Sections.SelectMany(s => s.Fields)
                .Single(f => f.Key == "startTime").Read(actual));
            Assert.Equal(TablePayloadHasher.Sha256Hex(actual), version.PayloadSha256Hex);
            return time;
        }).ToArray();
        Assert.True(times.Distinct().Count() > 20);
        Assert.Equal(original, TableDistributionService.GetEffectivePayload(source.Versions.Single()));
    }

    [Fact]
    public void An_explicit_single_second_can_repair_an_invalid_time_and_round_trip_without_other_changes()
    {
        var raw = new byte[21]; raw[0] = 35; raw[4] = 255; raw[11] = 19;
        var source = Set(raw);
        Assert.Throws<InvalidOperationException>(() => RoutineCallInRandomizationPlan.Create(source, new("source-time", Seed: Seed)));
        var plan = RoutineCallInRandomizationPlan.Create(source, new("window", "23:59:59", "23:59:59", Seed));
        var time = plan.TimeFor(Guid.Empty);
        Assert.Equal("23:59:59", time);
        var result = plan.SourceFor(source, time);
        var changed = TableDistributionService.GetEffectivePayload(result.Versions.Single());
        Assert.Equal(new byte[] { 23, 59, 59 }, changed[4..7]);
        Assert.Equal(raw[..4], changed[..4]);
        Assert.Equal(raw[7..], changed[7..]);
        Assert.Same(result, RoutineCallInRandomizationPlan.Create(result, new("window", time, time, Seed)).SourceFor(result, time));
    }

    [Theory]
    [InlineData("window", "23:00", "01:00")]
    [InlineData("window", "24:00", "24:00")]
    [InlineData("window", "02:00:00.1", "03:00")]
    [InlineData("source-time", "00:00", "01:00")]
    [InlineData("unknown", null, null)]
    public void Invalid_ranges_and_modes_are_rejected(string mode, string? start, string? end)
    {
        var raw = new byte[21]; raw[0] = 35;
        Assert.Throws<InvalidOperationException>(() => RoutineCallInRandomizationPlan.Create(Set(raw), new(mode, start, end, Seed)));
    }

    [Fact]
    public void Missing_seed_duplicate_record_and_unknown_length_are_rejected()
    {
        var raw = new byte[21]; raw[0] = 35;
        var source = Set(raw);
        Assert.Throws<InvalidOperationException>(() => RoutineCallInRandomizationPlan.Create(source, new("source-time")));
        Assert.Throws<InvalidOperationException>(() => RoutineCallInRandomizationPlan.Create(Set([35, 1]), new("source-time", Seed: Seed)));
        source.Versions.Add(Set(raw).Versions.Single());
        Assert.Throws<InvalidOperationException>(() => RoutineCallInRandomizationPlan.Create(source, new("source-time", Seed: Seed)));
    }

    private static TableSet Set(byte[] raw)
    {
        var definition = new TableDefinition { Name = "Synthetic routine settings", TableNumber = 35 };
        var payload = new TablePayload { RawContent = raw, Sha256Hex = TablePayloadHasher.Sha256Hex(raw), LengthBytes = raw.Length };
        return new TableSet { Name = "Synthetic configuration", Versions = [new TableVersion
        {
            TableDefinition = definition, TableDefinitionId = definition.Id, TablePayload = payload,
            TablePayloadId = payload.Id, PayloadSha256Hex = payload.Sha256Hex, TableRevision = 1
        }] };
    }
}
