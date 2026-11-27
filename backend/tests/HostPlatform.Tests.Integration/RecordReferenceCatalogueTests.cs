using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class RecordReferenceCatalogueTests(ApiFixture factory)
{
    private readonly HttpClient _client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);

    [Fact]
    public async Task Reference_separates_native_layouts_firmware_evidence_and_operational_messages()
    {
        var catalogue = await _client.GetFromJsonAsync<JsonElement>("/api/tables/reference-records");
        var rows = catalogue.EnumerateArray().ToDictionary(row => row.GetProperty("number").GetInt32());
        Assert.All(NativeTableLayouts.ReferenceLayouts(), layout =>
            Assert.Contains(rows[layout.Number].GetProperty("layouts").EnumerateArray(), item =>
                item.GetProperty("length").GetInt32() == layout.Length && item.GetProperty("profile").GetString() == layout.Profile));
        Assert.All(FirmwareTableProfiles.Catalog, profile =>
        {
            foreach (var (number, length) in profile.VerifiedLengths)
                Assert.Contains(rows[number].GetProperty("firmware").EnumerateArray(), item =>
                    item.GetProperty("profileId").GetString() == profile.Id && item.GetProperty("length").GetInt32() == length
                    && item.GetProperty("terminalAcknowledged").GetBoolean() == profile.TerminalAcknowledgedRecords.Contains(number));
        });

        Assert.All(LegacyTerminalTableRequirements.ReferencedRecordNumbers(), number => Assert.True(rows.ContainsKey(number)));
        var status = rows[10];
        Assert.Equal("Terminal status", status.GetProperty("name").GetString());
        Assert.Equal("operational", status.GetProperty("kind").GetString());
        Assert.Empty(status.GetProperty("layouts").EnumerateArray());
        Assert.Equal("operational", rows[20].GetProperty("kind").GetString());
        Assert.Equal("operational", rows[153].GetProperty("kind").GetString());
        Assert.Equal("scheduler_record", rows[94].GetProperty("kind").GetString());
        Assert.Equal("User interface parameters", rows[30].GetProperty("name").GetString());
        Assert.Equal("native_editor", rows[92].GetProperty("kind").GetString());
        Assert.Contains("free screening rule", rows[92].GetProperty("behaviorObservation").GetString());
        Assert.Equal(JsonValueKind.Null, rows[133].GetProperty("behaviorObservation").ValueKind);
        Assert.Contains(rows[92].GetProperty("layouts").EnumerateArray(), row => row.GetProperty("length").GetInt32() == 3401);
        Assert.Contains(rows[92].GetProperty("layouts").EnumerateArray(), row => row.GetProperty("length").GetInt32() == 3061);
        foreach (var number in new[] { 26,30,31,33,62 })
        {
            var native = rows[number].GetProperty("layouts").EnumerateArray().ToArray();
            Assert.NotEmpty(native);
            Assert.All(native, layout => {
                Assert.True(layout.GetProperty("explicitAuthoringAvailable").GetBoolean());
                Assert.False(layout.GetProperty("draftTemplateAvailable").GetBoolean());
            });
        }

        var definitions = await _client.GetFromJsonAsync<JsonElement>("/api/tables/definitions");
        foreach (var definition in definitions.EnumerateArray())
            Assert.Contains(rows[definition.GetProperty("tableNumber").GetInt32()].GetProperty("registeredDefinitions").EnumerateArray(),
                row => row.GetProperty("id").GetGuid() == definition.GetProperty("id").GetGuid()
                    && row.GetProperty("name").GetString() == definition.GetProperty("name").GetString());
    }

    [Fact]
    public void Unclassified_custom_number_is_not_promoted_to_a_verified_configuration()
    {
        var row = Assert.Single(RecordReferenceCatalogue.Build([250]).Where(record => record.Number == 250));
        Assert.Equal("registered_unclassified", row.Kind);
        Assert.Empty(row.Layouts);
        Assert.Empty(row.Firmware);
    }
}
