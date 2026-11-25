using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

internal static class DisplayFeatureFixture
{
    public static bool Later(string profile) => profile != FirmwareTableProfiles.MultiPay200;
    public static byte[] Record(string profile)
    {
        var bytes = new byte[72]; bytes[0] = 26; bytes[1] = 2; bytes[2] = 1;
        bytes[22] = 0xA5; bytes[24] = 0xD1;
        if (Later(profile)) { bytes[10] = 0x21; bytes[12] = 0xA2; }
        else bytes[12] = 0xA2;
        return bytes;
    }
    public static Dictionary<string, string> Changes(string profile)
    {
        var changes = new Dictionary<string, string> { ["flashServiceLevel"] = "true", ["spare.display.bit3"] = "true",
            ["spare.advertising.bit6"] = "true" };
        if (Later(profile)) changes["languageCount"] = "1";
        return changes;
    }
    public static byte[] Expected(string profile, byte[] source)
    {
        var bytes = source.ToArray(); bytes[22] |= 0x40; bytes[24] |= 0x0A;
        if (Later(profile)) bytes[12] = 0xA1;
        return bytes;
    }
}

public sealed class DisplayFeaturePreservationNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200, false)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180, false)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180, false)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180, true)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180, true)]
    public async Task Selected_display_options_deliver_only_expected_bytes_on_original_batch(string profile, bool languageSwap)
    {
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var record = DisplayFeatureFixture.Record(profile);
        if (languageSwap) record[11] = 0xF3;
        var expected = languageSwap ? record.ToArray() : DisplayFeatureFixture.Expected(profile, record);
        if (languageSwap) expected[10] = 0x12;
        var changes = languageSwap ? new Dictionary<string, string> { ["languageFirst"] = "2", ["languageSecond"] = "1" }
            : DisplayFeatureFixture.Changes(profile);
        var definition = new TableDefinition { Name = "Synthetic display options", TableNumber = 26 };
        var firmware = new FirmwareVersion { Label = "Synthetic display target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic display configuration", TargetFirmwareVersionId = firmware.Id };
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, record, 1));
        var unrelated = new TableDefinition { Name = "Unrelated service restriction", TableNumber = 58 };
        var unrelatedBytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(58)));
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(unrelated, unrelatedBytes, 2));
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic display receiver", TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id;
            db.AddRange(definition, firmware, set, unrelated, phone); await db.SaveChangesAsync();
        }
        var path = $"/api/tables/sets/{set.Id}/records/26/fields";
        var read = await fx.Client.GetFromJsonAsync<JsonElement>(path);
        using (var saved = await fx.Client.PutAsJsonAsync(path, new { revision = read.GetProperty("revision").GetInt32(),
            sha256 = read.GetProperty("sha256").GetString(), profile = read.GetProperty("profile").GetString(),
            changes })) saved.EnsureSuccessStatusCode();
        using (var published = await fx.Client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();
        using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phoneId}/downloads", new
        { tableSetId = set.Id, scope = "Partial", partialTableDefinitionIds = new[] { definition.Id } });
        queued.EnsureSuccessStatusCode();
        var batch = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port); var stream = tcp.GetStream();
            await stream.WriteAsync(Packet(0, [44, 1])); AssertAck(await Read(stream));
            var header = await Read(stream); Assert.Equal(new byte[] { 18 }, header.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)));
            var content = await Read(stream); Assert.Equal(expected, content.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | content.Control)));
            await stream.WriteAsync(Packet(1, [14, 26])); AssertAck(await Read(stream));
            var end = await Read(stream); Assert.Equal(new byte[] { 13 }, end.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
        }
        JsonElement completed = default;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            completed = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batch}");
            if (completed.GetProperty("status").GetInt32() == 2) break;
            await Task.Delay(100);
        }
        Assert.Equal(2, completed.GetProperty("status").GetInt32());
        Assert.Equal("ack", Assert.Single(completed.GetProperty("timeline").EnumerateArray().ToArray()).GetProperty("lastAckStatus").GetString());
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(batch, Assert.Single(await db.DownloadBatches.Where(row => row.TerminalId == phoneId).ToListAsync()).Id);
        }
        byte[] Packet(byte control, byte[]? data = null) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] });
    }

    private static void AssertAck(NccWirePacket packet)
    { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }
    private static async Task<NccWirePacket> Read(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var header = new byte[3];
        await stream.ReadExactlyAsync(header, timeout.Token); var frame = new byte[NccFrameLayout.GetExpectedWireLength(header[2])];
        header.CopyTo(frame, 0); await stream.ReadExactlyAsync(frame.AsMemory(3), timeout.Token);
        var decoded = NccFrameCodec.TryDecode(frame, NccParseMode.Strict); Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics)); return decoded.Packet!;
    }
}

[Collection("PostgreSQL integration")]
public sealed class PostgresDisplayFeaturePreservationTests
{
    [PostgresIntegrationFact]
    public async Task Display_edit_preserves_other_bits_language_order_and_noop_hash()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        foreach (var profile in new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.LaterMultiPay180, FirmwareTableProfiles.GraphicalMultiPay180 })
        {
            var input = DisplayFeatureFixture.Record(profile);
            if (DisplayFeatureFixture.Later(profile)) input[11] = 0xF3;
            var expected = DisplayFeatureFixture.Expected(profile, input);
            var definition = new TableDefinition { Name = "Synthetic display options", TableNumber = 26 };
            var firmware = new FirmwareVersion { Label = "Synthetic display target", TableProfileId = profile };
            var set = new TableSet { Name = "Synthetic display configuration", TargetFirmwareVersionId = firmware.Id };
            var version = CarrierSchedulerSpareFixture.Version(definition, input, 1); set.Versions.Add(version);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.AddRange(definition, firmware, set); await db.SaveChangesAsync();
            }
            var path = $"/api/tables/sets/{set.Id}/records/26/fields";
            var snapshot = await Read(); var display = snapshot.GetProperty("sections").EnumerateArray().Single(section => section.GetProperty("key").GetString() == "display");
            var fields = display.GetProperty("fields").EnumerateArray().ToDictionary(field => field.GetProperty("key").GetString()!);
            Assert.Equal("false", fields["flashServiceLevel"].GetProperty("value").GetString());
            Assert.True(fields["spare.display.bit3"].GetProperty("isSpare").GetBoolean());
            var unchanged = new Dictionary<string,string> { ["flashServiceLevel"] = "false", ["displayNumber"] = "true" };
            if (DisplayFeatureFixture.Later(profile)) { Assert.Equal("2", fields["languageCount"].GetProperty("value").GetString()); unchanged["languageCount"] = "2"; }
            using (var saved = await Save(snapshot, unchanged)) saved.EnsureSuccessStatusCode(); await Check(input, 1);
            var currentRevision = 1;
            if (DisplayFeatureFixture.Later(profile))
            {
                var swapped = input.ToArray(); swapped[10] = 0x12;
                var changes = new Dictionary<string, string> { ["languageFirst"] = "2", ["languageSecond"] = "1" };
                using (var moved = await Save(snapshot, changes)) moved.EnsureSuccessStatusCode(); await Check(swapped, ++currentRevision);
                using (var staleMove = await Save(snapshot, changes)) Assert.Equal(HttpStatusCode.Conflict, staleMove.StatusCode);
                snapshot = await Read();
                using (var noop = await Save(snapshot, changes)) noop.EnsureSuccessStatusCode(); await Check(swapped, currentRevision);
                using (var reversed = await Save(snapshot, new() { ["languageFirst"] = "1", ["languageSecond"] = "2" })) reversed.EnsureSuccessStatusCode();
                await Check(input, ++currentRevision);
                snapshot = await Read();
            }
            using (var rejected = await Save(snapshot, new() { ["spare.display.bit1"] = "true" })) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using (var rejected = await Save(snapshot, new() { ["languageCount"] = "5", ["flashServiceLevel"] = "true" })) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); await Check(input, currentRevision);
            using (var edited = await Save(snapshot, DisplayFeatureFixture.Changes(profile))) edited.EnsureSuccessStatusCode(); await Check(expected, ++currentRevision);
            using (var stale = await Save(snapshot, DisplayFeatureFixture.Changes(profile))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            snapshot = await Read(); using (var saved = await Save(snapshot, DisplayFeatureFixture.Changes(profile))) saved.EnsureSuccessStatusCode(); await Check(expected, currentRevision);
            using (var published = await client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();

            Task<JsonElement> Read() => client.GetFromJsonAsync<JsonElement>(path);
            Task<HttpResponseMessage> Save(JsonElement observed, Dictionary<string,string> changes) => client.PutAsJsonAsync(path, new
            { revision = observed.GetProperty("revision").GetInt32(), sha256 = observed.GetProperty("sha256").GetString(), profile = observed.GetProperty("profile").GetString(), changes });
            async Task Check(byte[] bytes, int revision)
            {
                await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var saved = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.Id == version.Id);
                Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(saved)); Assert.Equal(revision, saved.TableRevision);
                Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), saved.PayloadSha256Hex);
            }
        }
    }
}
