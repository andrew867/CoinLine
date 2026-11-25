using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TerminalIdCutoverTests
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180)]
    public void Substitution_preserves_every_byte_except_the_five_id_bytes(string profileId)
    {
        var profile = FirmwareTableProfiles.Find(profileId)!;
        const string current = "0000000001";
        const string next = "9999999998";
        var original = Enumerable.Repeat((byte)0xA5, profile.VerifiedLengths[21]).ToArray();
        original[0] = 21;
        Convert.FromHexString(current).CopyTo(original, 1);
        var record = new TableVersion { TableDefinition = new TableDefinition { TableNumber = 21 }, EmbeddedPayload = original };
        var saved = original.ToArray();

        var result = TerminalIdCutover.RewriteAccessRecord(record, profile, current, next);

        Assert.Equal(saved, record.EmbeddedPayload);
        Assert.Equal(result.Length, saved.Length);
        Assert.Equal(saved[0], result[0]);
        Assert.Equal(saved[6..], result[6..]);
        Assert.Equal(Convert.FromHexString(next), result[1..6]);
        Assert.Equal(5, saved.Zip(result).Count(pair => pair.First != pair.Second));
        Assert.Throws<InvalidOperationException>(() => TerminalIdCutover.RewriteAccessRecord(record, profile, next, current));
        Assert.Throws<InvalidOperationException>(() => TerminalIdCutover.RewriteAccessRecord(record, null, current, next));
        record.DependsOnTableDefinitionId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => TerminalIdCutover.RewriteAccessRecord(record, profile, current, next));
    }

    [Theory]
    [InlineData("0000010123", "000001", "000002", "0000020123")]
    [InlineData("0000010123", "0000010123", "0000020456", "0000020456")]
    [InlineData("0000010123", "0000010124", "0000020456", null)]
    [InlineData("000001012A", "000001", "000002", null)]
    [InlineData("0000010123", "000001", "000001", null)]
    [InlineData("0000010123", "000001", "0000020456", null)]
    [InlineData("0000010123", "000001", "00000A", null)]
    [InlineData("0000010123", "000001", "０００００２", null)]
    public void Only_distinct_decimal_exact_or_prefix_mappings_are_supported(string original,
        string source, string replacement, string? expected) =>
        Assert.Equal(expected, TerminalIdCutover.Map(original, source, replacement));
}
