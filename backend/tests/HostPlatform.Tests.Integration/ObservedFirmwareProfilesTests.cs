using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresObservedFirmwareProfilesTests
{
    [PostgresIntegrationFact]
    public async Task Observed_lengths_enforce_size_without_assuming_an_ambiguous_native_layout()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var password = Guid.NewGuid().ToString("N");
        var account = new OperatorAccount
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Synthetic profile operator",
            Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int>
            { ["firmware-registry"] = 5, ["table-sets"] = 7, ["table-records"] = 7 })
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.OperatorAccounts.Add(account);
            await db.SaveChangesAsync();
        }
        using (var login = await client.PostAsJsonAsync("/api/auth/login", new { username = account.Username, password }))
        {
            login.EnsureSuccessStatusCode();
            var session = await login.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Operator", session.GetProperty("role").GetString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        }
        var list = await client.GetFromJsonAsync<JsonElement>("/api/firmware/table-profiles");
        var observed = list.EnumerateArray().Where(row => row.GetProperty("observedLengthsOnly").GetBoolean()).ToArray();
        Assert.Equal(10, observed.Length);
        Assert.All(observed, row => Assert.False(row.GetProperty("completeCatalogue").GetBoolean()));
        var early20 = Assert.Single(observed, row => row.GetProperty("id").GetString() == FirmwareTableProfiles.Mtr20EarlyMultiPay);
        Assert.Contains("firmware error", early20.GetProperty("evidenceNote").GetString());

        foreach (var (target, editor, screening, length, slots) in new[]
        {
            (FirmwareTableProfiles.Msr17Desk, NativeTableLayouts.MultiPay, 24, 721, 60),
            (FirmwareTableProfiles.Mtr120CoinBasic, NativeTableLayouts.Later, 92, 3061, 180)
        })
        {
            using var registered = await client.PostAsJsonAsync("/api/firmware/versions", new
            { label = "Synthetic observed target", tableProfileId = target });
            registered.EnsureSuccessStatusCode();
            var firmwareId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var definition = new TableDefinition { TableNumber = 26, Name = "Synthetic features" };
            var wrongDefinition = new TableDefinition { TableNumber = screening, Name = "Synthetic wrong screening length" };
            var advertising = new TableDefinition { TableNumber = 29, Name = "Synthetic optional advertising" };
            var input = new byte[72]; input[0] = 26; input[24] = 0xA0;
            var version = CarrierSchedulerSpareFixture.Version(definition, input, 1);
            var set = new TableSet { Name = "Synthetic observed configuration", TargetFirmwareVersionId = firmwareId };
            set.Versions.Add(version);
            var optional = AdvertisingTableWire.Encode(Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false, true),
                AdvertisingTableWire.SlotCount).ToArray());
            set.Versions.Add(CarrierSchedulerSpareFixture.Version(advertising, optional, 2));
            var wrongSet = new TableSet { Name = "Synthetic mismatched observed configuration", TargetFirmwareVersionId = firmwareId };
            var wrong = new byte[1021]; wrong[0] = (byte)screening;
            wrongSet.Versions.Add(CarrierSchedulerSpareFixture.Version(wrongDefinition, wrong, 1));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                db.AddRange(definition, wrongDefinition, advertising, set, wrongSet);
                await db.SaveChangesAsync();
            }
            var root = $"/api/tables/sets/{set.Id}";
            var fields = root + "/records/26/fields";
            var unknown = await client.GetFromJsonAsync<JsonElement>(fields);
            Assert.False(unknown.GetProperty("supported").GetBoolean());
            Assert.True(unknown.GetProperty("observedLengthsOnly").GetBoolean());
            Assert.Contains("length alone", unknown.GetProperty("reason").GetString());
            Assert.Equal(2, unknown.GetProperty("profiles").GetArrayLength());
            var snapshot = await Read(editor);
            Assert.True(snapshot.GetProperty("supported").GetBoolean());
            Assert.True(snapshot.GetProperty("observedLengthsOnly").GetBoolean());
            Assert.Equal(editor, snapshot.GetProperty("profile").GetString());
            Assert.Equal(2, snapshot.GetProperty("profiles").GetArrayLength());
            using (var refused = await Save(snapshot, null, new() { ["displayNumber"] = "true" }))
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            await Check(input, 1);
            using (var noop = await Save(snapshot, editor, new())) noop.EnsureSuccessStatusCode();
            await Check(input, 1);
            var expected = input.ToArray(); expected[24] |= 1;
            using (var edited = await Save(snapshot, editor, new() { ["displayNumber"] = "true" })) edited.EnsureSuccessStatusCode();
            await Check(expected, 2);
            using (var stale = await Save(snapshot, editor, new() { ["displayNumber"] = "false" }))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var chosenAgain = await client.GetFromJsonAsync<JsonElement>(fields);
            Assert.False(chosenAgain.GetProperty("supported").GetBoolean());

            var screenPath = root + $"/records/{screening}/fields";
            var templates = await client.GetFromJsonAsync<JsonElement>(screenPath);
            var template = Assert.Single(templates.GetProperty("templates").EnumerateArray());
            Assert.Equal(length, template.GetProperty("length").GetInt32());
            Assert.Equal(slots, template.GetProperty("screeningSlots").GetInt32());
            using (var initialized = await client.PostAsJsonAsync(screenPath, template)) initialized.EnsureSuccessStatusCode();
            var screen = await client.GetFromJsonAsync<JsonElement>(screenPath);
            Assert.True(screen.GetProperty("supported").GetBoolean());
            Assert.Equal(slots, screen.GetProperty("sections").GetArrayLength());
            Assert.Equal(screening == 24 ? CallScreeningWire.CompactProfile : NativeTableLayouts.Later,
                screen.GetProperty("profile").GetString());
            if (screening == 24)
            {
                var family = await client.GetFromJsonAsync<TerminalFamilyTableReview>(root + "/family-review?familyId=msr17-desk-generic");
                Assert.Equal(50, family!.ScreeningEntries);
                var screeningEvidence = Assert.Single(Assert.Single(family.Tables, row => row.Name == "Call screening").Records);
                Assert.Equal("verifiedLength", screeningEvidence.Format);
                Assert.Equal(new[] { 60 }, screeningEvidence.ScreeningSlots);
            }

            var rateTemplate = await client.GetFromJsonAsync<JsonElement>(root + "/records/73/fields");
            Assert.Equal(2, rateTemplate.GetProperty("templates").GetArrayLength());
            var review = await client.GetFromJsonAsync<JsonElement>(root);
            Assert.True(review.GetProperty("tableProfileEvidence").GetProperty("observedLengthsOnly").GetBoolean());
            var findings = review.GetProperty("firmwareFindings").EnumerateArray().ToArray();
            Assert.Contains(findings, row => row.GetProperty("tableNumber").GetInt32() == 26 &&
                !row.GetProperty("blocking").GetBoolean() && row.GetProperty("message").GetString()!.Contains("native field layout"));
            Assert.Contains(findings, row => row.GetProperty("tableNumber").GetInt32() == 29 &&
                !row.GetProperty("blocking").GetBoolean() && row.GetProperty("message").GetString()!.Contains("no verified length"));
            using (var published = await client.PostAsync(root + "/publish?confirm=true", null)) published.EnsureSuccessStatusCode();
            var wrongPath = $"/api/tables/sets/{wrongSet.Id}";
            var incompatible = await client.GetFromJsonAsync<JsonElement>(wrongPath + $"/records/{screening}/fields");
            Assert.False(incompatible.GetProperty("supported").GetBoolean());
            Assert.Contains("Import the matching record", incompatible.GetProperty("reason").GetString());
            using (var rejected = await client.PostAsync(wrongPath + "/publish?confirm=true", null))
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            Task<JsonElement> Read(string profile) => client.GetFromJsonAsync<JsonElement>(fields + "?profile=" + Uri.EscapeDataString(profile));
            Task<HttpResponseMessage> Save(JsonElement observedFields, string? profile, Dictionary<string, string> changes) =>
                client.PutAsJsonAsync(fields, new
                {
                    revision = observedFields.GetProperty("revision").GetInt32(),
                    sha256 = observedFields.GetProperty("sha256").GetString(), profile, changes
                });
            async Task Check(byte[] bytes, int revision)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var saved = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.Id == version.Id);
                Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(saved));
                Assert.Equal(revision, saved.TableRevision);
                Assert.Equal(TablePayloadHasher.Sha256Hex(bytes), saved.PayloadSha256Hex);
            }
        }
    }
}

public sealed class ObservedFirmwareProfilesNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.Msr17Desk, 24, 721)]
    [InlineData(FirmwareTableProfiles.Mtr120CoinBasic, 92, 3061)]
    public async Task Selected_observed_screening_record_is_transferred_and_acknowledged_on_original_batch(string target, int number, int length)
    {
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var template = NativeTableLayouts.DraftTemplates(number).Single(layout => layout.Length == length);
        var input = NativeTableLayouts.CreateDraftTemplate(template);
        var layout = NativeTableLayouts.Find(number, input, template.Profile)!;
        input = layout.Apply(input, new Dictionary<string, string>
        { ["rule-0.pattern"] = "5550100", [number == 24 ? "rule-0.reference" : "rule-0.rate"] = "255" });
        var expected = input.ToArray(); expected[1] |= 1;
        var definition = new TableDefinition { Name = "Synthetic observed screening", TableNumber = number };
        var unrelated = new TableDefinition { Name = "Synthetic unrelated tariff", TableNumber = 73 };
        var tariff = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(73).First());
        var firmware = new FirmwareVersion { Label = "Synthetic observed target", TableProfileId = target };
        var set = new TableSet { Name = "Synthetic observed screening configuration", TargetFirmwareVersionId = firmware.Id };
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, input, 1));
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(unrelated, tariff, 2));
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var phone = new Terminal { SiteId = (await db.Sites.FirstAsync()).Id, DisplayName = "Synthetic observed receiver",
                TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id;
            db.AddRange(firmware, definition, unrelated, set, phone);
            await db.SaveChangesAsync();
        }
        var path = $"/api/tables/sets/{set.Id}/records/{number}/fields";
        var snapshot = await fx.Client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(template.Profile, snapshot.GetProperty("profile").GetString());
        using (var edited = await fx.Client.PutAsJsonAsync(path, new
        {
            revision = snapshot.GetProperty("revision").GetInt32(), sha256 = snapshot.GetProperty("sha256").GetString(),
            profile = template.Profile, changes = new Dictionary<string, string> { ["rule-0.free"] = "true" }
        })) edited.EnsureSuccessStatusCode();
        using (var published = await fx.Client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();
        using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phoneId}/downloads", new
        { tableSetId = set.Id, scope = "Partial", partialTableDefinitionIds = new[] { definition.Id } });
        queued.EnsureSuccessStatusCode();
        var batch = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Packet(0, [44, 1])); AssertAck(await Read(stream));
            var header = await Read(stream); Assert.Equal(new byte[] { 18 }, header.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)));
            var received = new List<byte>();
            while (received.Count < expected.Length)
            {
                var fragment = await Read(stream); received.AddRange(fragment.Data);
                await stream.WriteAsync(Packet((byte)(NccControl.Ack | fragment.Control)));
            }
            Assert.Equal(expected, received);
            await stream.WriteAsync(Packet(1, [14, (byte)number])); AssertAck(await Read(stream));
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
        Assert.Equal("ack", Assert.Single(completed.GetProperty("timeline").EnumerateArray()).GetProperty("lastAckStatus").GetString());
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(batch, Assert.Single(await db.DownloadBatches.Where(row => row.TerminalId == phoneId).ToListAsync()).Id);
        }

        byte[] Packet(byte control, byte[]? data = null) =>
            NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] });
    }

    private static void AssertAck(NccWirePacket packet)
    { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }
    private static async Task<NccWirePacket> Read(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var header = new byte[3]; await stream.ReadExactlyAsync(header, timeout.Token);
        var frame = new byte[NccFrameLayout.GetExpectedWireLength(header[2])]; header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(3), timeout.Token);
        var decoded = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics));
        return decoded.Packet!;
    }
}
