using System.Buffers.Binary;
using System.Security.Cryptography;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class GraphicalStarterWireTests
{
    [Fact]
    public void Starter_has_sixteen_distinct_frames_with_a_closed_global_link_sequence()
    {
        var records = GraphicalStarterWire.Create();
        Assert.Equal(new[] { 131, 132, 133 }, records.Keys);
        Assert.All(records.Values, data => Assert.Equal(9224, data.Length));
        var digests = new HashSet<string>();
        for (var global = 0; global < 16; global++)
        {
            var record = records[131 + global / 8];
            var start = 8 + (global % 8) * 1152;
            Assert.Equal((byte)'B', record[start]);
            Assert.True(digests.Add(Convert.ToHexString(SHA256.HashData(record.AsSpan(start + 1, 1008)))));
            Assert.All(record[(start + 1009)..(start + 1045)], value => Assert.Equal(0, value));
            Assert.Equal(50, BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(start + 1045)));
            Assert.Equal((global + 1) % 16, record[start + 1058]);
            var layout = NativeTableLayouts.Find(record[0], record)!;
            Assert.Equal(record, layout.Apply(record, new Dictionary<string, string>()));
        }
        Assert.Equal(0x09, records[131][3]);
        Assert.Equal(255, records[131][4]);
        Assert.Equal(255, records[131][5]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(records[131].AsSpan(1)));
        Assert.Equal((byte)'M', records[133][8]);
        Assert.Equal(records[131], GraphicalStarterWire.Create()[131]);
    }

    [Fact]
    public void Optional_menu_has_only_local_navigation_and_does_not_modify_the_animation_records()
    {
        var defaults = GraphicalStarterWire.Create();
        var menu = GraphicalStarterWire.Create(enableSampleMenu: true);
        Assert.Equal(0x0B, menu[131][3]);
        Assert.Equal(16, menu[131][4]);
        Assert.Equal(255, menu[131][5]);
        Assert.Equal(defaults[131][8..], menu[131][8..]);
        Assert.Equal(defaults[132], menu[132]);
        Assert.Equal(defaults[133], menu[133]);
        var layout = NativeTableLayouts.Find(133, menu[133])!;
        var fields = layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        Assert.Equal("Info", fields["screen-0.panel-0.label-0"].Read(menu[133]));
        Assert.Equal("80", fields["screen-0.panel-0.button-0.next"].Read(menu[133]));
        Assert.Equal("144", fields["screen-0.panel-0.button-1.next"].Read(menu[133]));
        Assert.Equal("16", fields["screen-0.panel-1.button-0.next"].Read(menu[133]));
        Assert.All(fields.Values.Where(field => field.Key.EndsWith(".digits")),
            field => Assert.Equal("", field.Read(menu[133])));
    }
}
