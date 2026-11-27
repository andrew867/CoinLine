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
public sealed class PostgresManagementAccessAuthoringTests
{
    [PostgresIntegrationFact]
    public async Task Missing_access_can_be_authored_for_confirmed_profiles_without_assignment_or_download()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("Security:Mode", "Token")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        using var anonymous = factory.CreateClient();
        var readerAccount = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-read-{Guid.NewGuid():N}", "Operator");
        var early = Set(FirmwareTableProfiles.MultiPay200); var later = Set(FirmwareTableProfiles.GraphicalMultiPay180);
        var observed = Set(FirmwareTableProfiles.Mtr211MultiPay); var untargeted = Set(null); var published = Set(null);
        published.Status = TableSetStatus.Published;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.True(db.Database.IsNpgsql());
            db.AddRange(early, later, observed, untargeted, published);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == readerAccount.Id)).PermissionProfileJson = "{\"table-records\":1}";
            await db.SaveChangesAsync();
        }
        using var reader = TestOperatorAuthentication.CreateAuthenticatedClient(factory, readerAccount.Username, "Operator");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path(early))).StatusCode);
        Assert.Equal(1, (await Templates(reader, early)).GetArrayLength());
        Assert.Equal(18, (await Templates(admin, early))[0].GetProperty("length").GetInt32());
        Assert.Equal(48, (await Templates(admin, later))[0].GetProperty("length").GetInt32());
        Assert.Equal(0, (await Templates(admin, observed)).GetArrayLength());
        Assert.Equal(2, (await Templates(admin, untargeted)).GetArrayLength());
        Assert.Equal(0, (await Templates(admin, published)).GetArrayLength());

        var input = new ManagementAccessDraftInput("2025550100", "2025550101", "2025550102");
        object Request(int length, ManagementAccessDraftInput? values) => new { profile = length == 18 ? NativeTableLayouts.MultiPay : NativeTableLayouts.Later, length, managementAccess = values };
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Path(early), Request(18, input))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Path(early), Request(48, input))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Path(early), Request(18, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Path(early), Request(18, input with { TerminalNumber = "invalid" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Path(untargeted), Request(40, input))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Path(observed), Request(48, input))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Path(published), Request(18, input))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(Path(early), Request(18, input))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(Path(later), Request(48, input with { CadId = "01020304", CpeId = "AABBCCDD" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Path(early), Request(18, input))).StatusCode);
        var fields = await admin.GetFromJsonAsync<JsonElement>(Path(later));
        Assert.True(fields.GetProperty("supported").GetBoolean());
        var values = fields.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray()).ToDictionary(field => field.GetProperty("key").GetString()!);
        Assert.Equal(input.TerminalNumber, values["terminalNumber"].GetProperty("value").GetString());
        Assert.Equal(input.Primary, values["primary"].GetProperty("value").GetString());
        Assert.Equal("01020304", values["cadId"].GetProperty("value").GetString());
        var revision = fields.GetProperty("revision").GetInt32(); var sha256 = fields.GetProperty("sha256").GetString();
        using (var unchanged = await admin.PutAsJsonAsync(Path(later), new { revision, sha256, profile = NativeTableLayouts.Later, changes = new Dictionary<string, string>() }))
            unchanged.EnsureSuccessStatusCode();
        var same = await admin.GetFromJsonAsync<JsonElement>(Path(later));
        Assert.Equal(revision, same.GetProperty("revision").GetInt32()); Assert.Equal(sha256, same.GetProperty("sha256").GetString());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var rawEarly = await Read(early.Id); var rawLater = await Read(later.Id);
            Assert.Equal(Convert.FromHexString("1520255501002A2555A1A1002A2555A1A200"), rawEarly);
            Assert.Equal(Convert.FromHexString("1520255501002A2555A1A100000000002A2555A1A2000000000001020304AABBCCDD"), rawLater[..34]);
            Assert.All(rawLater[34..], value => Assert.Equal((byte)0, value));
            Assert.False(await db.TableVersions.AnyAsync(row => row.TableSetId == observed.Id || row.TableSetId == untargeted.Id || row.TableSetId == published.Id));
            Assert.False(await db.TerminalTableAssignments.AnyAsync(row => row.TableSetId == early.Id || row.TableSetId == later.Id));
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TableSetId == early.Id || row.TableSetId == later.Id));
            async Task<byte[]> Read(Guid setId) => TableDistributionService.GetEffectivePayload(await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == setId));
        }
        using (var edit = await admin.PutAsJsonAsync(Path(later), new { revision, sha256, profile = NativeTableLayouts.Later,
            changes = new Dictionary<string, string> { ["alternate"] = "2025550103" } })) edit.EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var stored = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == later.Id);
            var expected = new byte[48];
            Convert.FromHexString("1520255501002A2555A1A100000000002A2555A1A3000000000001020304AABBCCDD").CopyTo(expected, 0);
            Assert.Equal(expected, TableDistributionService.GetEffectivePayload(stored));
            Assert.Equal(revision + 1, stored.TableRevision);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TableSetId == early.Id || row.TableSetId == later.Id));
        }

        static TableSet Set(string? profile) => new() { Name = "Fixture access " + Guid.NewGuid().ToString("N"),
            TargetFirmwareVersion = profile == null ? null : new FirmwareVersion { Label = "Fixture firmware " + Guid.NewGuid().ToString("N"), TableProfileId = profile } };
        static string Path(TableSet set) => $"/api/tables/sets/{set.Id}/records/21/fields";
        static async Task<JsonElement> Templates(HttpClient client, TableSet set) => (await client.GetFromJsonAsync<JsonElement>(Path(set))).GetProperty("managementAccessTemplates");
    }
}
