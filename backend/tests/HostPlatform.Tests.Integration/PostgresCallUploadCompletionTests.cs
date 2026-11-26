using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresCallUploadCompletionTests
{
    [PostgresIntegrationFact]
    public async Task Explicit_upload_completes_after_all_records_and_replays_final_response_without_a_new_download()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString()).UseSetting("Ncc:LoopbackOnly", "true"));
        using var api = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        api.DefaultRequestHeaders.Add("X-API-Key", key);
        var (phone, setId) = await Seed(factory.Services);
        var wire = Convert.FromHexString(phone.TerminalIdHex);
        var queued = await AccessRolloutFixture.Post(api, $"/api/terminals/{phone.Id}/downloads", new { tableSetId = setId });
        var queuedId = queued.GetProperty("id").GetGuid();
        var status = new byte[11]; status[0] = 10;
        var version = new byte[28]; version[0] = 60;
        "FIX0001"u8.CopyTo(version.AsSpan(1)); "99A "u8.CopyTo(version.AsSpan(8)); version[23] = 2;
        var first = Call(0x0246); var second = Call(0x0247);
        byte[] firstFrame = [.. first, .. second, .. Statistic(56, 81), .. status, .. version];
        Assert.Equal(228, firstFrame.Length);
        var carriers = Enumerable.Range(0, 4).Select(i =>
        {
            var report = Statistic(57, 190); report[13] = (byte)(i + 1); return report;
        }).ToArray();
        byte[] final = [.. Statistic(37, 99), 13];
        byte[] receipts = [5, 0x46, 0x02, 5, 0x47, 0x02];

        for (var pass = 0; pass < 2; pass++)
        {
            using var socket = await Begin();
            var stream = socket.GetStream();
            await Send(stream, 1, firstFrame);
            AssertAck(await Read(stream));
            var receipt = await Read(stream);
            Assert.Equal(receipts, receipt.Data);
            await Send(stream, (byte)(NccControl.Ack | receipt.Control));
            for (var i = 0; i < carriers.Length; i++)
            {
                await Send(stream, (byte)((i + 2) % 4), carriers[i]);
                AssertAck(await Read(stream));
            }
            await Send(stream, 2, final);
            AssertAck(await Read(stream));
            var completion = await Read(stream);
            Assert.Equal(new byte[] { 13 }, completion.Data);

            // Retry the final original application frame, then its header-only form.
            await Send(stream, (byte)(2 | NccControl.ReTrans), final);
            AssertAck(await Read(stream));
            var replay = await Read(stream);
            Assert.Equal(completion.Control | NccControl.ReTrans, replay.Control);
            Assert.Equal(completion.Data, replay.Data);
            await stream.WriteAsync(HeaderOnly(2));
            AssertAck(await Read(stream));
            var headerReplay = await Read(stream);
            Assert.Equal(replay.Control, headerReplay.Control);
            Assert.Equal(completion.Data, headerReplay.Data);
            await Send(stream, (byte)(NccControl.Ack | NccControl.Clr | completion.Control));
        }

        // A reconnect after the records have been cleared sends only status and MT13.
        using (var socket = await Begin())
        {
            var stream = socket.GetStream();
            await Send(stream, 1, [.. status, .. version, 13]);
            AssertAck(await Read(stream));
            var end = await Read(stream);
            Assert.Equal(new byte[] { 13 }, end.Data);
            await Send(stream, (byte)(NccControl.Ack | NccControl.Clr | end.Control));
        }
        // Cashbox records can be present in the same explicit upload and are
        // independently observable without retaining the report contents.
        using (var socket = await Begin())
        {
            var box = new byte[57]; box[0] = 38;
            new byte[] { 128, 2, 29, 10, 11, 12 }.CopyTo(box, 1);
            Convert.FromHexString("00001001").CopyTo(box, 8);
            BinaryPrimitives.WriteInt32LittleEndian(box.AsSpan(13), 25);
            var collection = new byte[71]; collection[0] = 51;
            Convert.FromHexString("00001002").CopyTo(collection, 5);
            BinaryPrimitives.WriteUInt16LittleEndian(collection.AsSpan(9), 0x0248);
            BinaryPrimitives.WriteInt32LittleEndian(collection.AsSpan(11), 25);
            box.AsSpan(1).CopyTo(collection.AsSpan(15));
            var stream = socket.GetStream();
            await Send(stream, 1, [.. box, .. collection, 13]);
            AssertAck(await Read(stream));
            var end = await Read(stream);
            Assert.Equal(new byte[] { 13 }, end.Data);
            await Send(stream, (byte)(NccControl.Ack | NccControl.Clr | end.Control));
        }
        // An incomplete explicit report is never given an application completion.
        using (var socket = await Begin())
        {
            var stream = socket.GetStream();
            await Send(stream, 1, first[..^1]);
            await Assert.ThrowsAsync<EndOfStreamException>(() => Read(stream));
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var uploads = await db.UploadBatches.Include(row => row.Records).Where(row => row.TerminalId == phone.Id).ToListAsync();
        Assert.Equal(2, uploads.Count(row => TerminalUploads.IsCallDetail(row.IdempotencyKey)));
        Assert.Equal(6, uploads.Count(row => TerminalUploads.IsStatistics(row.IdempotencyKey)));
        Assert.All(uploads, row =>
        {
            Assert.Empty(row.RawPayload); Assert.Equal(UploadBatchStatus.Completed, row.Status);
            Assert.Empty(Assert.Single(row.Records).RawPayload);
        });
        var callSequences = uploads.Where(row => TerminalUploads.IsCallDetail(row.IdempotencyKey))
            .Select(row => TerminalUploads.ReadMetadata<DlogCallDetailWire.CallDetail>(row.Records.Single().DecodedMetadataJson)!.Sequence);
        Assert.Equal(new ushort[] { 0x0246, 0x0247 }, callSequences.Order());
        var frames = await db.NccSessionFrames.Where(row => row.NccSession!.TerminalId == phone.Id).ToListAsync();
        Assert.All(frames.Where(row => !row.SentByHost && row.WireLength is 239 or 201 or 111 or 51 or 64), row => Assert.Empty(row.RawBytes));
        var observations = new List<JsonElement>();
        foreach (var sessionId in frames.Select(row => row.NccSessionId).Distinct())
        {
            var detail = await api.GetFromJsonAsync<JsonElement>($"/api/ncc/sessions/{sessionId}/upload-observations");
            Assert.Single(detail.EnumerateObject());
            var recorded = detail.GetProperty("callUploadFrames").EnumerateArray().ToArray();
            foreach (var observation in recorded)
            {
                var sequence = observation.GetProperty("sequence").GetInt32();
                Assert.Empty(frames.Single(row => row.NccSessionId == sessionId && row.Sequence == sequence).RawBytes);
                Assert.DoesNotContain("5550196", observation.GetRawText());
                Assert.DoesNotContain("ABABAB", observation.GetRawText(), StringComparison.OrdinalIgnoreCase);
                Assert.All(observation.EnumerateObject(), property => Assert.Contains(property.Name,
                    new[] { "sequence", "parsed", "endsDialogue", "records" }));
                if (observation.GetProperty("records").ValueKind == JsonValueKind.Array)
                    Assert.All(observation.GetProperty("records").EnumerateArray(), record =>
                        Assert.Equal(new[] { "byteCount", "messageType" }, record.EnumerateObject().Select(property => property.Name).Order()));
            }
            observations.AddRange(recorded);
        }
        // Every original explicit upload frame must be correlated to its own session;
        // receipt retransmissions do not create new upload observations.
        Assert.Equal(15, observations.Count);
        Assert.Contains(observations, observation => Headers(observation).SequenceEqual(new[] { (53, 54), (53, 54), (56, 81), (10, 11), (60, 28) }));
        Assert.Contains(observations, observation => observation.GetProperty("endsDialogue").ValueKind == JsonValueKind.True &&
            Headers(observation).SequenceEqual(new[] { (10, 11), (60, 28), (13, 1) }));
        Assert.Contains(observations, observation => observation.GetProperty("endsDialogue").ValueKind == JsonValueKind.True &&
            Headers(observation).SequenceEqual(new[] { (38, 57), (51, 71), (13, 1) }));
        var unrecognized = Assert.Single(observations, observation => !observation.GetProperty("parsed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, unrecognized.GetProperty("records").ValueKind);
        Assert.Equal(JsonValueKind.Null, unrecognized.GetProperty("endsDialogue").ValueKind);
        using (var anonymous = factory.CreateClient())
        {
            using var denied = await anonymous.GetAsync($"/api/ncc/sessions/{frames[0].NccSessionId}/upload-observations");
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        var statusRecords = await db.DlogTransactions.Where(row => row.TerminalId == phone.Id && (row.MessageType == 10 || row.MessageType == 60)).ToListAsync();
        Assert.Contains(statusRecords, row => row.MessageType == 10 && row.RawPayload.Length == 11);
        Assert.Contains(statusRecords, row => row.MessageType == 60 && row.RawPayload.Length == 28);
        var batch = Assert.Single(await db.DownloadBatches.Where(row => row.TerminalId == phone.Id).ToListAsync());
        Assert.Equal(queuedId, batch.Id);
        Assert.Equal(DownloadBatchStatus.Pending, batch.Status);

        static IEnumerable<(int, int)> Headers(JsonElement observation) => observation.GetProperty("records").ValueKind == JsonValueKind.Array
            ? observation.GetProperty("records").EnumerateArray().Select(record => (record.GetProperty("messageType").GetInt32(), record.GetProperty("byteCount").GetInt32()))
            : [];

        async Task<TcpClient> Begin()
        {
            var socket = new TcpClient();
            await socket.ConnectAsync(IPAddress.Loopback, port);
            var stream = socket.GetStream();
            await Send(stream, 0, [42, 8, .. status, .. version]);
            AssertAck(await Read(stream));
            var request = await Read(stream);
            Assert.Equal(new byte[] { 17 }, request.Data);
            await Send(stream, (byte)(NccControl.Ack | request.Control));
            return socket;
        }
        Task Send(NetworkStream stream, byte control, byte[]? data = null) =>
            stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] })).AsTask();
        byte[] HeaderOnly(byte id)
        {
            byte[] frame = [NccConstants.FrameStart, (byte)(NccControl.ReTrans | id), 10, .. wire, 0, 0, NccConstants.FrameEnd];
            var crc = NccCrc16.Compute(frame.AsSpan(0, frame.Length - 3));
            frame[^3] = (byte)crc; frame[^2] = (byte)(crc >> 8); return frame;
        }
    }

    private static async Task<(Terminal, Guid)> Seed(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var customer = new Customer { Name = "Synthetic upload customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { CustomerId = customer.Id, Name = "Synthetic upload site", Code = Guid.NewGuid().ToString("N")[..8] };
        var firmware = new FirmwareVersion { Label = "Synthetic upload profile", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var phone = new Terminal { DisplayName = "Synthetic upload phone", SiteId = site.Id, FirmwareVersionId = firmware.Id,
            TerminalIdHex = "0" + string.Concat(RandomNumberGenerator.GetBytes(9).Select(n => n % 10)) };
        var definition = new TableDefinition { TableNumber = 50, Name = "Synthetic pending coin record" };
        var set = new TableSet { Name = "Synthetic pending configuration", CustomerId = customer.Id };
        var bytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(50).Single());
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, bytes, 1));
        TableDistributionService.MarkPublished(set, DateTime.UtcNow);
        db.AddRange(customer, site, firmware, phone, definition, set);
        db.TerminalTableAssignments.Add(new TerminalTableAssignment { TerminalId = phone.Id, TableSetId = set.Id,
            CustomerId = customer.Id, SiteId = site.Id });
        await db.SaveChangesAsync();
        return (phone, set.Id);
    }

    private static byte[] Call(ushort sequence)
    {
        var record = new byte[54]; record[0] = 53;
        DlogRateWire.EncodeRequest("5550196", international: false).AsSpan(3, 10).CopyTo(record.AsSpan(2));
        record.AsSpan(13, 10).Fill(0xAB); record.AsSpan(43, 8).Fill(0xAB);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(23), 25);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(27), 5);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(31), sequence);
        new byte[] { 128, 2, 29, 10, 11, 12, 0, 1, 5, 0x33 }.CopyTo(record, 33);
        return record;
    }

    private static byte[] Statistic(byte type, int length)
    {
        var record = new byte[length]; record[0] = type;
        new byte[] { 128, 2, 29, 10, 11, 12, 128, 3, 1, 10, 11, 12 }.CopyTo(record, 1);
        return record;
    }

    private static void AssertAck(NccWirePacket packet)
    {
        Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack);
    }
    private static async Task<NccWirePacket> Read(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var header = new byte[3];
        await stream.ReadExactlyAsync(header, timeout.Token);
        var frame = new byte[NccFrameLayout.GetExpectedWireLength(header[2])];
        header.CopyTo(frame, 0); await stream.ReadExactlyAsync(frame.AsMemory(3), timeout.Token);
        var decoded = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics)); return decoded.Packet!;
    }
}
