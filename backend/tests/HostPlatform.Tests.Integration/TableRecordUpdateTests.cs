using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class TableRecordUpdateTests(ApiFixture factory)
{
    private readonly HttpClient client = CreateClient(factory);

    [Fact]
    public async Task Rollout_resumes_from_the_blocked_phone_and_can_skip_it_without_replaying_the_prefix()
    {
        var site = (await Get("sites"))[0].GetProperty("id").GetGuid();
        var group = await Create("terminal-groups", new { name = "Recoverable record update group" });
        var firmware = await Create("firmware/versions", new { label = "Rollout fixture firmware", tableProfileId = FirmwareTableProfiles.MultiPay200 });
        var ignored = await Phone("Before starting ID", "0000000670", group, firmware, site);
        var first = await Phone("First rollout phone", "0000000671", group, firmware, site);
        var blocked = await Phone("First blocked phone", "0000000672", group, firmware, site);
        var skipped = await Phone("Second blocked phone", "0000000673", group, firmware, site);
        var last = await Phone("Last rollout phone", "0000000674", group, firmware, site);
        var messages = await Definition("Rollout advertising", 29);
        var basis = await Set("Rollout original configuration", firmware);
        var source = await Set("Rollout source configuration", firmware);
        await Version(basis, messages, Bytes(29, 481, 1), 0);
        await Version(source, messages, Bytes(29, 481, 2), 0);
        await Publish(basis);
        await Publish(source);
        foreach (var phone in new[] { ignored, first, last })
            await Assign(phone, basis);

        var endpoint = $"tables/sets/{source}/record-updates";
        var request = new
        {
            targetType = "group", targetId = group, sourceRevision = (await Get($"tables/sets/{source}")).GetProperty("revision"),
            copyRecords = new[] { 29 }, removeRecords = Array.Empty<int>(), stopOnIssue = true,
            beginWithTerminalId = "0000000671", previewToken = (string?)null, rolloutId = (Guid?)null
        };
        var preview = await Post($"{endpoint}/preview", request);
        Assert.Equal(new[] { first, blocked, skipped, last },
            preview.GetProperty("targets").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));
        Assert.Equal(1, preview.GetProperty("changeCount").GetInt32());
        Assert.Equal(3, preview.GetProperty("pendingCount").GetInt32());
        var runId = preview.GetProperty("rolloutId").GetGuid();
        var confirmation = request with { previewToken = Token(preview), rolloutId = runId };
        var started = (await Post(endpoint, confirmation)).GetProperty("run");
        Assert.Equal(blocked, Blocked(started));
        var firstSet = (await Assignment(first)).GetProperty("tableSetId").GetGuid();
        Assert.NotEqual(basis, firstSet);
        Assert.Equal(basis, (await Assignment(last)).GetProperty("tableSetId").GetGuid());
        Assert.Equal(runId, Assert.Single((await Get($"{endpoint}/runs")).EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(0, (await Post(endpoint, confirmation)).GetProperty("changed").GetInt32());

        var joinedLater = await Phone("Joined after preview", "0000000675", group, firmware, site);
        await Assign(joinedLater, basis);
        await Assign(blocked, basis);
        var control = $"{endpoint}/runs/{runId}";
        var retried = (await Post(control, new { revision = started.GetProperty("revision"), decision = "retry" })).GetProperty("run");
        Assert.Equal(skipped, Blocked(retried));
        Assert.Equal(2, retried.GetProperty("changedCount").GetInt32());
        using var stale = await client.PostAsJsonAsync($"/api/{control}", new { revision = started.GetProperty("revision"), decision = "skip" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var completed = (await Post(control, new { revision = retried.GetProperty("revision"), decision = "skip" })).GetProperty("run");
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.Equal(4, completed.GetProperty("processed").GetInt32());
        Assert.Equal(3, completed.GetProperty("changedCount").GetInt32());
        Assert.Equal(1, completed.GetProperty("skippedCount").GetInt32());
        Assert.Equal(firstSet, (await Assignment(first)).GetProperty("tableSetId").GetGuid());
        Assert.Equal(basis, (await Assignment(first)).GetProperty("previousTableSetId").GetGuid());
        Assert.NotEqual(basis, (await Assignment(last)).GetProperty("tableSetId").GetGuid());
        Assert.Equal(basis, (await Assignment(ignored)).GetProperty("tableSetId").GetGuid());
        Assert.Equal(basis, (await Assignment(joinedLater)).GetProperty("tableSetId").GetGuid());
        using var skippedAssignment = await client.GetAsync($"/api/terminals/{skipped}/table-assignment");
        Assert.Equal(HttpStatusCode.NotFound, skippedAssignment.StatusCode);
        Assert.Empty((await Get($"{endpoint}/runs")).EnumerateArray());

        var stopRequest = request with { beginWithTerminalId = "0000000670" };
        var stopPreview = await Post($"{endpoint}/preview", stopRequest);
        Assert.Equal(1, stopPreview.GetProperty("changeCount").GetInt32());
        var stopId = stopPreview.GetProperty("rolloutId").GetGuid();
        var beforeStop = (await Post(endpoint, stopRequest with { previewToken = Token(stopPreview), rolloutId = stopId })).GetProperty("run");
        Assert.Equal(skipped, Blocked(beforeStop));
        var appliedBeforeStop = (await Assignment(ignored)).GetProperty("tableSetId").GetGuid();
        Assert.NotEqual(basis, appliedBeforeStop);
        var stopped = (await Post($"{endpoint}/runs/{stopId}",
            new { revision = beforeStop.GetProperty("revision"), decision = "stop" })).GetProperty("run");
        Assert.Equal("Stopped", stopped.GetProperty("status").GetString());
        Assert.Equal(3, stopped.GetProperty("processed").GetInt32());
        Assert.Equal(appliedBeforeStop, (await Assignment(ignored)).GetProperty("tableSetId").GetGuid());
        Assert.Equal(basis, (await Assignment(joinedLater)).GetProperty("tableSetId").GetGuid());
        Assert.Empty((await Get($"{endpoint}/runs")).EnumerateArray());

        static Guid Blocked(JsonElement run) => run.GetProperty("blockedPhone").GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Group_update_keeps_each_basis_and_rejects_an_outdated_preview()
    {
        var site = (await Get("sites"))[0].GetProperty("id").GetGuid();
        var group = await Create("terminal-groups", new { name = "Record update group" });
        var firmware = await Create("firmware/versions", new { label = "Mixed group firmware", tableProfileId = FirmwareTableProfiles.MultiPay200 });
        var first = await Phone("Record update fixture A", "0000000401", group, firmware, site);
        var second = await Phone("Record update fixture B", "0000000402", null, firmware, site);
        var unassigned = await Phone("Record update fixture C", "0000000403", group, firmware, site);
        var otherGroups = await Get($"terminals/{second}/groups");
        using var member = await client.PutAsJsonAsync($"/api/terminals/{second}/groups", new
        {
            revision = otherGroups.GetProperty("revision"), groupIds = new[] { group }
        });
        member.EnsureSuccessStatusCode();

        var access = await Definition("Group update terminal identity", 21);
        var messages = await Definition("Group update advertising", 29);
        var coins = await Definition("Group update coin settings", 50);
        var carrier = await Definition("Group update carrier screens", 23);
        var features = await Definition("Group update feature configuration", 26);
        var basisA = await Set("First original configuration", firmware);
        var basisB = await Set("Second original configuration", firmware);
        foreach (var (basis, wire, sample) in new[] { (basisA, "0000000401", (byte)10), (basisB, "0000000402", (byte)15) })
        {
            var identity = new byte[18];
            identity[0] = 21;
            Convert.FromHexString(wire).CopyTo(identity, 1);
            await Version(basis, access, identity, 0);
            await Version(basis, messages, Bytes(29, 481, sample), 1);
            await Version(basis, coins, Bytes(50, 105, sample), 2);
            await Version(basis, carrier, Bytes(23, 678, sample), 3);
            await Publish(basis);
        }
        await Assign(first, basisA);
        await Assign(second, basisB);

        var source = await Set("Saved advertising and features", firmware);
        await Version(source, features, Bytes(26, 72, 2), 0);
        await Version(source, messages, Bytes(29, 481, 75), 1, features);
        await Publish(source);
        var donor = await Get($"tables/sets/{source}");
        var revision = donor.GetProperty("revision").GetInt32();
        var endpoint = $"tables/sets/{source}/record-updates";

        using var region = await client.PutAsJsonAsync($"/api/terminals/{first}/inventory",
            new { profile = new { regionCode = "Update-East" } });
        region.EnsureSuccessStatusCode();
        var regional = await Post($"{endpoint}/preview", new
        {
            targetType = "group", targetId = group, regionCode = "update-east", sourceRevision = revision,
            copyRecords = new[] { 29 }, removeRecords = Array.Empty<int>()
        });
        Assert.Equal(1, regional.GetProperty("changeCount").GetInt32());
        Assert.Equal(first, Assert.Single(regional.GetProperty("targets").EnumerateArray()).GetProperty("id").GetGuid());

        var protectedPreview = await Post($"{endpoint}/preview", Request([], [50]));
        Assert.Equal(0, protectedPreview.GetProperty("changeCount").GetInt32());
        Assert.Contains("not confirmed optional", Target(protectedPreview, first).GetProperty("reason").GetString());

        var initial = await Post($"{endpoint}/preview", Request([29], [23]));
        Assert.Equal(2, initial.GetProperty("changeCount").GetInt32());
        Assert.Equal(2, initial.GetProperty("setCount").GetInt32());
        Assert.Contains("Assign a published", Target(initial, unassigned).GetProperty("reason").GetString());
        Assert.Contains(Target(initial, first).GetProperty("changes").EnumerateArray(),
            value => value.GetProperty("tableNumber").GetInt32() == 26 && value.GetProperty("required").GetBoolean());
        Assert.Equal(basisA, (await Assignment(first)).GetProperty("tableSetId").GetGuid());

        await Assign(second, basisB);
        using var stale = await client.PostAsJsonAsync($"/api/{endpoint}", Request([29], [23], Token(initial)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(basisA, (await Assignment(first)).GetProperty("tableSetId").GetGuid());
        Assert.Equal(basisB, (await Assignment(second)).GetProperty("tableSetId").GetGuid());

        var latest = await Post($"{endpoint}/preview", Request([29], [23]));
        var applied = await Post(endpoint, Request([29], [23], Token(latest)));
        Assert.Equal(2, applied.GetProperty("changed").GetInt32());
        Assert.Equal(2, applied.GetProperty("sets").GetArrayLength());
        foreach (var (phone, basis) in new[] { (first, basisA), (second, basisB) })
        {
            var assignment = await Assignment(phone);
            Assert.Equal(basis, assignment.GetProperty("previousTableSetId").GetGuid());
            var changed = await Get($"tables/sets/{assignment.GetProperty("tableSetId").GetGuid()}");
            var original = await Get($"tables/sets/{basis}");
            Assert.Equal(1, changed.GetProperty("status").GetInt32());
            Assert.Equal(firmware, changed.GetProperty("targetFirmwareVersionId").GetGuid());
            Assert.Equal(Hash(original, 21), Hash(changed, 21));
            Assert.Equal(Hash(original, 50), Hash(changed, 50));
            Assert.Equal(Hash(donor, 29), Hash(changed, 29));
            Assert.Equal(features, Record(changed, 29).GetProperty("dependsOnTableDefinitionId").GetGuid());
            Assert.DoesNotContain(changed.GetProperty("versions").EnumerateArray(), row => row.GetProperty("tableNumber").GetInt32() == 23);
            Assert.Equal(4, original.GetProperty("versions").GetArrayLength());
        }

        var noChanges = await Post($"{endpoint}/preview", Request([29], [23]));
        Assert.Equal(0, noChanges.GetProperty("changeCount").GetInt32());
        using var rollback = await client.PostAsync($"/api/terminals/{first}/table-assignment/rollback?confirm=true", null);
        rollback.EnsureSuccessStatusCode();
        Assert.Equal(basisA, (await Assignment(first)).GetProperty("tableSetId").GetGuid());

        object Request(int[] copyRecords, int[] removeRecords, string? previewToken = null) => new
        {
            targetType = "group", targetId = group, sourceRevision = revision, copyRecords, removeRecords, previewToken
        };
    }

    private static HttpClient CreateClient(ApiFixture factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Operator-Id", "record-update-test");
        client.DefaultRequestHeaders.Add("X-Operator-Role", "Admin");
        return client;
    }

    private Task<Guid> Phone(string displayName, string terminalIdHex, Guid? terminalGroupId, Guid firmwareVersionId, Guid siteId) =>
        Create("terminals", new { displayName, terminalIdHex, terminalGroupId, firmwareVersionId, siteId, status = 0 });

    private Task<Guid> Set(string name, Guid targetFirmwareVersionId) => Create("tables/sets", new { name, targetFirmwareVersionId });
    private Task<Guid> Definition(string name, int tableNumber) => Create("tables/definitions", new { name, tableNumber });

    private Task<JsonElement> Version(Guid set, Guid definition, byte[] bytes, int sortOrder, Guid? dependency = null) =>
        Post("tables/versions", new
        {
            tableSetId = set, tableDefinitionId = definition, tableRevision = 1, sortOrder,
            payloadBase64 = Convert.ToBase64String(bytes), dependsOnTableDefinitionId = dependency
        });

    private async Task Publish(Guid set)
    {
        using var response = await client.PostAsync($"/api/tables/sets/{set}/publish?confirm=true", null);
        response.EnsureSuccessStatusCode();
    }

    private Task<JsonElement> Assign(Guid phone, Guid tableSetId) => Post($"terminals/{phone}/table-assignment", new { tableSetId });
    private Task<JsonElement> Assignment(Guid phone) => Get($"terminals/{phone}/table-assignment");
    private Task<JsonElement> Get(string route) => client.GetFromJsonAsync<JsonElement>($"/api/{route}");
    private async Task<Guid> Create(string route, object request) => (await Post(route, request)).GetProperty("id").GetGuid();

    private async Task<JsonElement> Post(string route, object request)
    {
        using var response = await client.PostAsJsonAsync($"/api/{route}", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static byte[] Bytes(byte number, int length, byte value)
    {
        var bytes = new byte[length];
        bytes[0] = number;
        bytes[1] = value;
        return bytes;
    }

    private static JsonElement Target(JsonElement result, Guid id) => result.GetProperty("targets").EnumerateArray()
        .Single(target => target.GetProperty("id").GetGuid() == id);
    private static string Token(JsonElement result) => result.GetProperty("previewToken").GetString()!;
    private static JsonElement Record(JsonElement set, int number) => set.GetProperty("versions").EnumerateArray()
        .Single(version => version.GetProperty("tableNumber").GetInt32() == number);
    private static string? Hash(JsonElement set, int number) => Record(set, number).GetProperty("payloadSha256Hex").GetString();
}
