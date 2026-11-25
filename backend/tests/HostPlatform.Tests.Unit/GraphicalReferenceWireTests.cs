using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class GraphicalReferenceWireTests
{
    [Fact]
    public void Starter_rotation_and_local_menu_have_resolved_stored_links()
    {
        var records = GraphicalStarterWire.Create(enableSampleMenu: true);
        var review = NativeTableLayouts.ReviewGraphicalLinks(records);
        Assert.All(review.Records, record => Assert.Equal("included", record.Availability));
        Assert.NotEmpty(review.References);
        Assert.DoesNotContain(review.References, reference => reference.Advisory != null);
        Assert.Equal("Screen 21, panel 1", Assert.Single(review.References, reference => reference.FieldKey == "offHookScreen").Target);
        var menu = Assert.Single(review.References, reference =>
            reference.SourceRecord == 133 && reference.FieldKey == "screen-4.panel-0.button-0.next");
        Assert.Equal("Screen 21, panel 2", menu.Target);
        Assert.Equal("resolved", menu.TargetState);
    }

    [Fact]
    public void Reference_review_distinguishes_partial_undefined_unmapped_and_panel_addresses()
    {
        var records = GraphicalStarterWire.Create(enableSampleMenu: true).ToDictionary(row => row.Key, row => row.Value.ToArray());
        Set(records[131], "screen-0.next", 84);
        Set(records[131], "screen-0.connected", 24);
        Set(records[131], "button-0.next", 63);
        Set(records[131], "button-1.next", 23);
        Set(records[131], "button-2.next", 72);
        Set(records[131], "button-3.next", 127);
        records[131][3] |= 4;
        records[132][8] = 0xE1;
        Set(records[131], "button-4.next", 8);
        var before = records.ToDictionary(row => row.Key, row => row.Value.ToArray());

        var review = NativeTableLayouts.ReviewGraphicalLinks(records);
        Assert.Equal("Screen 21, panel 2", Ref(review, "screen-0.next").Target);
        Assert.Equal("resolved", Ref(review, "screen-0.next").TargetState);
        Assert.Equal("unrecognized_address", Ref(review, "screen-0.connected").TargetState);
        Assert.Equal("builtin", Ref(review, "button-0.next").TargetState);
        Assert.Equal("undefined_screen", Ref(review, "button-1.next").TargetState);
        Assert.Equal("unknown_screen_kind", Ref(review, "button-2.next").TargetState);
        Assert.Equal("noncanonical_builtin", Ref(review, "button-3.next").TargetState);
        Assert.Equal("unknown_screen_kind", Ref(review, "button-4.next").TargetState);
        Assert.Equal("no_selection", Ref(review, "connectedScreen").TargetState);
        Assert.NotNull(Ref(review, "connectedScreen").Advisory);
        Assert.Equal("button-1", Ref(review, "button-1.next").SourceSectionKey);
        foreach (var (number, bytes) in before)
            Assert.Equal(bytes, records[number]);

        records[132][8] = (byte)'B';
        Assert.Equal("non_mixed_panel", Ref(NativeTableLayouts.ReviewGraphicalLinks(records), "button-2.next").TargetState);
        var duplicate = NativeTableLayouts.ReviewGraphicalLinks(records, new HashSet<int> { 133 });
        Assert.Equal("ambiguous_record", Ref(duplicate, "screen-0.next").TargetState);
        records[133] = [133, 0];
        Assert.Equal("incompatible_record", Ref(NativeTableLayouts.ReviewGraphicalLinks(records), "screen-0.next").TargetState);
        records.Remove(133);
        var partial = NativeTableLayouts.ReviewGraphicalLinks(records);
        Assert.Equal("missing_record", Ref(partial, "screen-0.next").TargetState);
        Assert.Contains("partial download", Ref(partial, "screen-0.next").Advisory);
        Assert.Equal("missing", Assert.Single(partial.Records, record => record.Number == 133).Availability);
        var firmware = new FirmwareVersion { TableProfileId = FirmwareTableProfiles.GraphicalMultiPay180 };
        var set = new TableSet { TargetFirmwareVersionId = firmware.Id, TargetFirmwareVersion = firmware };
        foreach (var (number, bytes) in records)
            set.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { TableNumber = number }, EmbeddedPayload = bytes });
        var findings = TableSetFirmwareCompatibility.Review(set).ToArray();
        Assert.NotEmpty(findings);
        Assert.All(findings, finding => Assert.False(finding.Blocking));
        Assert.Contains(findings, finding => finding.TableNumber == 131 && finding.Message.Contains("saved graphical reference"));
    }

    private static GraphicalLinkReference Ref(GraphicalLinkReview review, string key) =>
        Assert.Single(review.References, reference => reference.SourceRecord == 131 && reference.FieldKey == key);

    private static void Set(byte[] record, string key, byte value)
    {
        var layout = NativeTableLayouts.Find(record[0], record)!;
        var field = layout.Sections.SelectMany(section => section.Fields).Single(field => field.Key == key);
        record[field.Offset] = value;
    }
}
