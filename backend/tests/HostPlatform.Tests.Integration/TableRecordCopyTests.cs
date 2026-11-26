using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class TableRecordCopyTests(ApiFixture factory)
{
    private readonly HttpClient client = CreateClient(factory);

    [Fact]
    public async Task Copies_selected_records_and_required_dependencies_without_replacing_unselected_records()
    {
        var source = await CreateAsync("sets", new { name = "Saved display configuration" });
        var destination = await CreateAsync("sets", new { name = "Phone configuration draft" });
        var donorFeatures = await CreateAsync("definitions", new { name = "Donor terminal features", tableNumber = 26 });
        var existingFeatures = await CreateAsync("definitions", new { name = "Enabled terminal features", tableNumber = 26 });
        var advertising = await CreateAsync("definitions", new { name = "Text advertising", tableNumber = 29 });
        var coins = await CreateAsync("definitions", new { name = "Coin settings", tableNumber = 50 });

        await AddRecord(source, donorFeatures, 26, 72, 2, 0);
        await AddRecord(source, advertising, 29, 481, 17, 1, donorFeatures);
        await AddRecord(source, coins, 50, 105, 30, 2);
        await AddRecord(destination, existingFeatures, 26, 72, 1, 0);
        await AddRecord(destination, coins, 50, 105, 25, 1);
        var before = await Read(destination);
        var donor = await Read(source);
        var request = CopyRequest(source, donor, before, 29);
        var path = $"/api/tables/sets/{destination}/record-copy";

        using var preview = await client.PostAsJsonAsync($"{path}/preview", request);
        preview.EnsureSuccessStatusCode();
        var changes = (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("records");
        Assert.Equal(2, changes.GetArrayLength());
        Assert.Equal(26, changes[0].GetProperty("tableNumber").GetInt32());
        Assert.Equal("replace", changes[0].GetProperty("action").GetString());
        Assert.True(changes[0].GetProperty("required").GetBoolean());
        Assert.Equal("add", changes[1].GetProperty("action").GetString());
        Assert.Equal(2, (await Read(destination)).GetProperty("versions").GetArrayLength());

        using var copied = await client.PostAsJsonAsync(path, request);
        copied.EnsureSuccessStatusCode();
        var after = await Read(destination);
        var features = Record(after, 26);
        Assert.Equal(Record(before, 26).GetProperty("id").GetGuid(), features.GetProperty("id").GetGuid());
        Assert.Equal(2, features.GetProperty("tableRevision").GetInt32());
        Assert.Equal(Record(donor, 26).GetProperty("tablePayloadId").GetGuid(), features.GetProperty("tablePayloadId").GetGuid());
        Assert.Equal(existingFeatures, Record(after, 29).GetProperty("dependsOnTableDefinitionId").GetGuid());
        Assert.Equal(Record(before, 50).GetRawText(), Record(after, 50).GetRawText());
        Assert.Equal(donor.GetRawText(), (await Read(source)).GetRawText());

        using var stale = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var unchanged = await client.PostAsJsonAsync(path, CopyRequest(source, donor, after, 29));
        unchanged.EnsureSuccessStatusCode();
        Assert.Equal(Revision(after), Revision(await Read(destination)));
    }

    [Fact]
    public async Task A_record_copy_respects_current_source_revision_and_the_draft_boundary()
    {
        var source = await CreateAsync("sets", new { name = "Editable source" });
        var destination = await CreateAsync("sets", new { name = "Editable destination" });
        var definition = await CreateAsync("definitions", new { name = "Text advertising source", tableNumber = 29 });
        await AddRecord(source, definition, 29, 481, 5, 0);
        await AddRecord(destination, definition, 29, 481, 6, 0);
        var request = CopyRequest(source, await Read(source), await Read(destination), 29);
        var path = $"/api/tables/sets/{destination}/record-copy";

        using var rename = await client.PutAsJsonAsync($"/api/tables/sets/{source}", new { name = "Updated source", isDefault = false });
        rename.EnsureSuccessStatusCode();
        using var stale = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var publish = await client.PostAsync($"/api/tables/sets/{destination}/publish?confirm=true", null);
        publish.EnsureSuccessStatusCode();
        using var rejected = await client.PostAsJsonAsync(path, CopyRequest(source, await Read(source), await Read(destination), 29));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
    }

    private static HttpClient CreateClient(ApiFixture factory)
    {
        var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory, "record-copy-test", "Admin");
        return client;
    }

    private async Task<Guid> CreateAsync(string route, object body)
    {
        using var response = await client.PostAsJsonAsync($"/api/tables/{route}", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task AddRecord(Guid set, Guid definition, byte number, int length, byte sample, int order, Guid? dependency = null)
    {
        var bytes = new byte[length];
        bytes[0] = number;
        bytes[1] = sample;
        await CreateAsync("versions", new
        {
            tableSetId = set, tableDefinitionId = definition, tableRevision = 1, payloadBase64 = Convert.ToBase64String(bytes),
            sortOrder = order, dependsOnTableDefinitionId = dependency
        });
    }

    private Task<JsonElement> Read(Guid set) => client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{set}");
    private static int Revision(JsonElement set) => set.GetProperty("revision").GetInt32();
    private static JsonElement Record(JsonElement set, int number) => set.GetProperty("versions").EnumerateArray()
        .Single(version => version.GetProperty("tableNumber").GetInt32() == number);

    private static object CopyRequest(Guid source, JsonElement donor, JsonElement destination, params int[] recordNumbers) =>
        new { sourceSetId = source, sourceRevision = Revision(donor), destinationRevision = Revision(destination), recordNumbers };
}
