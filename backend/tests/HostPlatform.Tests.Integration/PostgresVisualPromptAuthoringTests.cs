using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresVisualPromptAuthoringTests
{
    [PostgresIntegrationFact]
    public async Task Missing_prompt_records_are_authored_with_exact_fixed_rows_and_permissions_without_assignments()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("Security:Mode", "Token")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        using var anonymous = factory.CreateClient();
        var readerAccount = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-reader-{Guid.NewGuid():N}", "Operator");
        var early = Set(FirmwareTableProfiles.MultiPay200); var later = Set(FirmwareTableProfiles.GraphicalMultiPay180);
        var untargeted = Set(null); var observed = Set(FirmwareTableProfiles.Mtr211MultiPay); var published = Set(null);
        published.Status = TableSetStatus.Published;
        var blank = Set(null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.True(db.Database.IsNpgsql());
            db.AddRange(early, later, untargeted, observed, published, blank);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == readerAccount.Id)).PermissionProfileJson = "{\"table-records\":1}";
            await db.SaveChangesAsync();
        }
        using var reader = TestOperatorAuthentication.CreateAuthenticatedClient(factory, readerAccount.Username, "Operator");
        var earlyPath = Path(early, 27); var laterPath = Path(later, 85);
        var example = new Dictionary<int, string?> { [0] = "Fixture", [1] = "Café", [2] = null };
        object Request(string profile, int length, Dictionary<int, string?>? rows) => new { profile, length, rows };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(earlyPath)).StatusCode);
        Assert.Equal(140, (await reader.GetFromJsonAsync<JsonElement>(earlyPath)).GetProperty("promptAuthoring").GetProperty("rows").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(earlyPath + "/prompt-authoring", Request(NativeTableLayouts.MultiPay, 2801, example))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(earlyPath + "/prompt-authoring", Request(NativeTableLayouts.Later, 8001, example))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(earlyPath + "/prompt-authoring", Request(NativeTableLayouts.MultiPay, 2801, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(earlyPath + "/prompt-authoring", Request(NativeTableLayouts.MultiPay, 2801, new() { [140] = "Invalid" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(earlyPath + "/prompt-authoring", Request(NativeTableLayouts.MultiPay, 2801, new() { [0] = "漢" }))).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await admin.GetFromJsonAsync<JsonElement>(Path(observed, 85))).GetProperty("promptAuthoring").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Path(observed, 85) + "/prompt-authoring", Request(NativeTableLayouts.Later, 8001, example))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Path(later, 98) + "/prompt-authoring", Request(NativeTableLayouts.Later, 8001, example))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Path(published, 85) + "/prompt-authoring", Request(NativeTableLayouts.Later, 8001, example))).StatusCode);

        var expected = new Dictionary<(Guid, int), byte[]>();
        foreach (var (set, number, count) in new[] { (early, 27, 140), (early, 28, 140), (later, 85, 400), (later, 86, 400), (untargeted, 98, 400), (untargeted, 99, 400) })
        {
            var path = Path(set, number); var result = await admin.GetFromJsonAsync<JsonElement>(path); var info = result.GetProperty("promptAuthoring");
            if (number == 28)
            {
                var companion = Assert.Single(result.GetProperty("visualPromptCompanions").EnumerateArray());
                Assert.Equal(27, companion.GetProperty("number").GetInt32()); Assert.Equal("compatible", companion.GetProperty("status").GetString());
                Assert.Equal("populated", companion.GetProperty("rows")[0].GetString()); Assert.Equal("blank", companion.GetProperty("rows")[2].GetString());
                Assert.DoesNotContain("Fixture", result.GetRawText());
            }
            var rowCount = info.GetProperty("rows").GetArrayLength(); Assert.Equal(count, rowCount);
            var length = info.GetProperty("length").GetInt32(); Assert.Equal(1 + count * 20, length);
            var profile = info.GetProperty("profile").GetString()!;
            var rows = new Dictionary<int, string?>(example) { [count - 1] = "End" };
            using var created = await admin.PostAsJsonAsync(path + "/prompt-authoring", Request(profile, length, rows));
            Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
            var bytes = Enumerable.Repeat((byte)' ', length).ToArray(); bytes[0] = (byte)number;
            System.Text.Encoding.ASCII.GetBytes("Fixture").CopyTo(bytes, 1);
            new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xA6 }.CopyTo(bytes, 21);
            System.Text.Encoding.ASCII.GetBytes("End").CopyTo(bytes, length - 20);
            expected.Add((set.Id, number), bytes);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(Path(blank, 27) + "/prompt-authoring", Request(NativeTableLayouts.MultiPay, 2801, new()))).StatusCode);
        var blankBytes = Enumerable.Repeat((byte)' ', 2801).ToArray(); blankBytes[0] = 27; expected.Add((blank.Id, 27), blankBytes);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(laterPath + "/prompt-authoring", Request(NativeTableLayouts.Later, 8001, example))).StatusCode);

        var snapshot = await admin.GetFromJsonAsync<JsonElement>(laterPath);
        var revision = snapshot.GetProperty("revision").GetInt32(); var sha256 = snapshot.GetProperty("sha256").GetString();
        using (var unchanged = await admin.PutAsJsonAsync(laterPath, new { revision, sha256, profile = NativeTableLayouts.Later, changes = new Dictionary<string, string>() }))
            unchanged.EnsureSuccessStatusCode();
        var same = await admin.GetFromJsonAsync<JsonElement>(laterPath);
        Assert.Equal(revision, same.GetProperty("revision").GetInt32()); Assert.Equal(sha256, same.GetProperty("sha256").GetString());
        using (var edit = await admin.PutAsJsonAsync(laterPath, new { revision, sha256, profile = NativeTableLayouts.Later,
            changes = new Dictionary<string, string> { ["prompt-1.text"] = "Cafè" } })) edit.EnsureSuccessStatusCode();
        expected[(later.Id, 85)][24] = 0xA5;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            foreach (var ((setId, number), bytes) in expected)
            {
                var stored = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == setId && row.TableDefinition!.TableNumber == number);
                Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(stored));
                Assert.Equal(setId == later.Id && number == 85 ? 2 : 1, stored.TableRevision);
            }
            Assert.False(await db.TableVersions.AnyAsync(row => row.TableSetId == observed.Id || row.TableSetId == published.Id));
            var authoredSetIds = new[] { early.Id, later.Id, untargeted.Id, blank.Id };
            Assert.False(await db.TerminalTableAssignments.AnyAsync(row => authoredSetIds.Contains(row.TableSetId)));
            Assert.False(await db.DownloadBatches.AnyAsync(row => authoredSetIds.Contains(row.TableSetId)));
            Assert.All(await db.TableSets.Where(row => authoredSetIds.Contains(row.Id)).ToListAsync(), set => Assert.Equal(TableSetStatus.Draft, set.Status));
        }

        static TableSet Set(string? profile) => new() { Name = "Fixture prompt " + Guid.NewGuid().ToString("N"),
            TargetFirmwareVersion = profile == null ? null : new FirmwareVersion { Label = "Fixture target " + Guid.NewGuid().ToString("N"), TableProfileId = profile } };
        static string Path(TableSet set, int number) => $"/api/tables/sets/{set.Id}/records/{number}/fields";
    }
}
