using HostPlatform.Protocols.Dlog;
using System.Text.Json;

namespace HostPlatform.Tests.Protocol;

public sealed class TerminalStatusWireTests
{
    [Fact]
    public void Named_asserted_and_clear_flags_are_distinguished_from_unmapped_bits_and_invalid_records()
    {
        byte[] raw = [10, 0x01, 0x23, 0x45, 0x67, 0x89, 0x05, 0, 0x40, 0x40, 0x80];
        var status = Assert.IsType<TerminalStatusWire.Status>(TerminalStatusWire.Read(raw));
        Assert.Equal("0123456789", status.SerialNumber);
        Assert.True(status.HasStatusBits);
        Assert.Equal(2, status.UnmappedSetBitCount);
        Assert.Equal("0500404080", status.RawStatusHex);
        Assert.Equal(new[] { 2, 39 }, status.Groups.SelectMany(group => group.UnresolvedFlags).Select(bit => bit.StatusIndex));
        var flags = status.Groups.SelectMany(group => group.Flags).ToArray();
        Assert.Equal(new[] { "Handset fault", "Memory lost", "Cash box full" }, flags.Where(flag => flag.Asserted).Select(flag => flag.Name));
        Assert.False(Assert.Single(flags, flag => flag.Name == "Telephony not responding").Asserted);
        using var decoded = JsonDocument.Parse(DlogPayloadClassifier.ToDecodedJson(DlogPayloadClassifier.Classify(raw, null, true)));
        var fields = decoded.RootElement.GetProperty("Fields");
        Assert.Equal(2, fields.GetProperty("unmappedStatusBits").GetInt32());
        Assert.Equal(3, fields.GetProperty("statusFlags").GetArrayLength());
        Assert.Equal("Handset fault, Memory lost, Cash box full, Unmapped status bits (2)", FieldEventSummary.TryBuild(10, raw));
        raw.AsSpan(6).Clear();
        Assert.False(TerminalStatusWire.Read(raw)!.HasStatusBits);
        raw[1] = 0xFA;
        Assert.Null(TerminalStatusWire.Read(raw)!.SerialNumber);
        raw[0] = 11;
        Assert.Null(TerminalStatusWire.Read(raw));
        Assert.Null(TerminalStatusWire.Read([10, 0]));
    }

    [Theory]
    [InlineData(TerminalStatusProfile.Unknown, 27)]
    [InlineData(TerminalStatusProfile.Earlier, 30)]
    [InlineData(TerminalStatusProfile.Later, 37)]
    public void Every_single_bit_is_preserved_as_either_a_named_condition_or_an_explicit_unresolved_mask(
        TerminalStatusProfile profile, int knownCount)
    {
        int[] shared = [0,1,7,8,9,10,12,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35];
        int[] extra = profile switch { TerminalStatusProfile.Earlier => [13,14,15], TerminalStatusProfile.Later => [2,3,4,5,6,11,13,14,37,38], _ => [] };
        var known = shared.Concat(extra).ToHashSet();
        Assert.Equal(knownCount, known.Count);
        for (var ordinal = 0; ordinal < 40; ordinal++)
        {
            byte[] raw = [10, 0,0,0,0,0,0,0,0,0,0];
            var mask = 1 << (ordinal % 8);
            raw[6 + ordinal / 8] = (byte)mask;
            var result = TerminalStatusWire.Read(raw, profile)!;
            Assert.True(result.HasStatusBits);
            Assert.Equal(profile.ToString(), result.Profile);
            Assert.Equal(Convert.ToHexString(raw.AsSpan(6)), result.RawStatusHex);
            var group = result.Groups[ordinal / 8];
            Assert.Equal(mask, group.RawMask);
            var named = result.Groups.SelectMany(g => g.Flags).Where(f => f.Asserted).ToArray();
            var unresolved = result.Groups.SelectMany(g => g.UnresolvedFlags).ToArray();
            if (known.Contains(ordinal))
            {
                Assert.Equal(ordinal, Assert.Single(named).StatusIndex); Assert.Empty(unresolved); Assert.Equal(0, result.UnmappedSetBitCount);
            }
            else
            {
                Assert.Empty(named); var bit = Assert.Single(unresolved); Assert.Equal(ordinal, bit.StatusIndex); Assert.Equal(mask, bit.Mask);
                Assert.NotEmpty(bit.Description); Assert.Equal(1, result.UnmappedSetBitCount);
            }
        }
        var clear = TerminalStatusWire.Read([10,0,0,0,0,0,0,0,0,0,0], profile)!;
        Assert.False(clear.HasStatusBits); Assert.Equal(0, clear.UnmappedSetBitCount);
    }

    [Fact]
    public void Later_status_and_alarm_names_are_qualified_without_guessing_reused_or_reserved_bits()
    {
        byte[] raw = [10, 0,0,0,0,0, 4,0,0,0,0];
        var later = TerminalStatusWire.Read(raw, TerminalStatusProfile.Later)!;
        Assert.Equal("EPM/SAM not responding", Assert.Single(later.Groups.SelectMany(g => g.Flags), f => f.Asserted).Name);
        Assert.Equal(0, later.UnmappedSetBitCount);
        Assert.Equal("EPM/SAM not responding", FieldEventSummary.TryBuild(10, raw, TerminalStatusProfile.Later));
        Assert.Contains("later profile: EPM/SAM not responding", Assert.Single(TerminalStatusWire.Read(raw)!.Groups[0].UnresolvedFlags).Description);
        raw[6] = 0; raw[7] = 0xE0; raw[10] = 0xF0;
        var early = TerminalStatusWire.Read(raw, TerminalStatusProfile.Earlier)!;
        Assert.Equal(new[] { "Operating error", "Nonfatal terminal logic error", "Fatal terminal error causing reboot" }, early.Groups[1].Flags.Where(f => f.Asserted).Select(f => f.Name));
        later = TerminalStatusWire.Read(raw, TerminalStatusProfile.Later)!;
        Assert.Equal(new[] { "Required configuration table missing", "Data-jack port blocked" }, later.Groups[1].Flags.Where(f => f.Asserted).Select(f => f.Name));
        Assert.Equal(new[] { "Collection upload dialogue failure", "Code-download server connection failure" }, later.Groups[4].Flags.Where(f => f.Asserted).Select(f => f.Name));
        Assert.Equal(new[] {15,36,39}, later.Groups.SelectMany(g => g.UnresolvedFlags).Select(f => f.StatusIndex));
        Assert.Contains("Profile-dependent", TerminalStatusWire.Read(raw)!.Groups[1].UnresolvedFlags[0].Description);
        Assert.Equal("Collection upload dialogue failure (alarm 37)", FieldEventSummary.TryBuild(16, [16,37]));
        Assert.Equal("Code-download server connection failure (alarm 38)", FieldEventSummary.TryBuild(16, [16,38]));
        foreach (var n in new byte[] {7,15,36,39}) Assert.Equal($"Unlisted alarm {n}", FieldEventSummary.TryBuild(16, [16,n]));
    }
}
