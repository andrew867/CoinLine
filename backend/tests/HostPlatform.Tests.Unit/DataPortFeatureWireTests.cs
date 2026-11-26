using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class DataPortFeatureWireTests
{
    [Theory]
    [InlineData(NativeTableLayouts.MultiPay)]
    [InlineData(NativeTableLayouts.Later)]
    public void Every_data_port_control_bit_is_independent_and_spares_have_no_assigned_meaning(string profile)
    {
        var record = Enumerable.Repeat((byte)0x5A, 72).ToArray(); record[0] = 26;
        record[9] = 15; record[54] = 0xA3; record[71] = 7;
        var layout = NativeTableLayouts.Find(26, record, profile)!;
        var section = layout.Sections.Single(s => s.Key == "dataPort");
        var flags = section.Fields.Where(f => f.Encoding == TableFieldEncoding.Flag).ToArray();
        Assert.Equal(new[] { 1, 2, 4, 8, 16, 32, 64, 128 }, flags.Select(f => f.Mask));
        Assert.All(flags, flag => Assert.Equal(54, flag.Offset));
        Assert.Equal(new[] { "spare.dataPort.bit5", "spare.dataPort.bit6", "spare.dataPort.bit7" }, flags.Where(f => f.IsSpare).Select(f => f.Key));
        Assert.Equal(record, layout.Apply(record, section.Fields.ToDictionary(f => f.Key, f => f.Read(record)!)));
        foreach (var flag in flags)
        {
            var expected = record.ToArray(); expected[54] ^= (byte)flag.Mask;
            var edited = layout.Apply(record, new Dictionary<string, string> { [flag.Key] = flag.Read(record) == "true" ? "false" : "true" });
            Assert.Equal(expected, edited);
        }
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { ["spare.dataPort.bit4"] = "true" }));
        Assert.Equal("15", section.Fields.Single(f => f.Key == "dataDisplayDelay").Read(record));
        Assert.Equal("7", section.Fields.Single(f => f.Key == "dataPortSettle").Read(record));
    }
}
