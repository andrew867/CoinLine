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
    public async Task Combined_cashbox_and_call_upload_attention_restores_only_known_current_status_before_transmit_request()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString()).UseSetting("Ncc:LoopbackOnly", "true"));
        using var api = TestOperatorAuthentication.CreateAuthenticatedClient(factory); api.DefaultRequestHeaders.Add("X-API-Key", key);
        var (known, setId) = await Seed(factory.Services); var (unknown, _) = await Seed(factory.Services);
        var queued = await AccessRolloutFixture.Post(api, $"/api/terminals/{known.Id}/downloads", new { tableSetId = setId });
        var box = new byte[DlogCashboxWire.StatusLength]; box[0] = 38;
        new byte[] { 128, 2, 29, 10, 11, 12 }.CopyTo(box, 1); box[7] = 1;
        new byte[] { 0, 0, 0, 0xA1 }.CopyTo(box, 8); box[12] = 5;
        BinaryPrimitives.WriteInt32LittleEndian(box.AsSpan(13), 25); BinaryPrimitives.WriteUInt16LittleEndian(box.AsSpan(17), 1);
        // Establish the baseline through the same genuine matched terminal report path.
        using (var socket = await Connect())
        {
            var stream = socket.GetStream(); await Send(stream, known, 0, box); AssertAck(await Read(stream));
            await Send(stream, known, NccControl.Clr);
        }
        var status = new byte[11]; status[0] = 10; var version = new byte[28]; version[0] = 60;
        byte[] attention = [42, 0x82, .. status, .. version];
        var call = Call(0x0249);
        for (var pass = 0; pass < 2; pass++)
        {
            using var socket = await Connect(); var stream = socket.GetStream();
            await Send(stream, known, 0, attention); AssertAck(await Read(stream));
            var response = await Read(stream); Assert.Equal([.. box, 17], response.Data);
            if (pass == 0)
            {
                // Both a full and header-only retransmission must replay the
                // original combined payload, not retrieve or fabricate a new status.
                await Send(stream, known, NccControl.ReTrans, attention); AssertAck(await Read(stream));
                var fullRetry = await Read(stream); Assert.Equal(response.Data, fullRetry.Data);
                Assert.Equal((byte)(response.Control | NccControl.ReTrans), fullRetry.Control);
                await stream.WriteAsync(HeaderOnly(known)); AssertAck(await Read(stream));
                var headerRetry = await Read(stream); Assert.Equal(response.Data, headerRetry.Data);
                Assert.Equal(fullRetry.Control, headerRetry.Control);
            }
            await Send(stream, known, (byte)(NccControl.Ack | NccControl.PacketId(response.Control)));
            await Send(stream, known, 1, [.. call, .. status, .. version, 13]); AssertAck(await Read(stream));
            var done = await Read(stream); Assert.Equal(new byte[] { 5, 0x49, 0x02, 13 }, done.Data);
            await Send(stream, known, (byte)(NccControl.Ack | NccControl.Clr | done.Control));
        }
        using (var socket = await Connect())
        {
            var stream = socket.GetStream(); await Send(stream, unknown, 0, attention); AssertAck(await Read(stream));
            var response = await Read(stream); Assert.Equal(new byte[] { 17 }, response.Data);
            await Send(stream, unknown, (byte)(NccControl.Ack | response.Control));
            await Send(stream, unknown, 1, [.. status, .. version, 13]); AssertAck(await Read(stream));
            var done = await Read(stream); Assert.Equal(new byte[] { 13 }, done.Data);
            await Send(stream, unknown, (byte)(NccControl.Ack | NccControl.Clr | done.Control));
        }
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        Assert.Single(await db.UploadBatches.Where(row => row.TerminalId == known.Id && row.IdempotencyKey.StartsWith(TerminalUploads.CallDetailPrefix)).ToListAsync());
        Assert.False(await db.UploadBatches.AnyAsync(row => row.TerminalId == unknown.Id));
        var reports = await db.DlogTransactions.Where(row => row.TerminalId == known.Id && row.MessageType == 38).ToListAsync();
        Assert.Equal(box, Assert.Single(reports).RawPayload);
        Assert.False(await db.DlogTransactions.AnyAsync(row => row.TerminalId == unknown.Id && row.MessageType == 38));
        var observations = await db.AuditEvents.Where(row => row.Action == "call_upload_cashbox_restore" && (row.TerminalId == known.Id || row.TerminalId == unknown.Id)).ToListAsync();
        Assert.Equal(3, observations.Count);
        foreach (var row in observations)
        {
            var data = JsonSerializer.Deserialize<JsonElement>(row.DetailJson);
            Assert.Equal(row.TerminalId == known.Id, data.GetProperty("available").GetBoolean());
            Assert.True(data.GetProperty("sent").GetBoolean()); Assert.Equal(row.TerminalId == known.Id ? 58 : 1, data.GetProperty("responseBytes").GetInt32());
            var detail = await api.GetFromJsonAsync<JsonElement>($"/api/ncc/sessions/{row.TerminalSessionId}/upload-observations");
            var restore = Assert.Single(detail.GetProperty("cashboxRestores").EnumerateArray());
            Assert.Equal(new[] { "available", "observedAtUtc", "responseBytes", "sent" }, restore.EnumerateObject().Select(field => field.Name).Order());
            Assert.Equal(row.TerminalId == known.Id, restore.GetProperty("available").GetBoolean());
        }
        var batch = Assert.Single(await db.DownloadBatches.Where(row => row.TerminalId == known.Id).ToListAsync());
        Assert.Equal(queued.GetProperty("id").GetGuid(), batch.Id); Assert.Equal(DownloadBatchStatus.Pending, batch.Status);

        async Task<TcpClient> Connect() { var socket = new TcpClient(); await socket.ConnectAsync(IPAddress.Loopback, port); return socket; }
        static Task Send(NetworkStream stream, Terminal phone, byte control, byte[]? data = null) => stream.WriteAsync(NccFrameCodec.Encode(
            new NccWirePacket { Control = control, TerminalId = Convert.FromHexString(phone.TerminalIdHex), Data = data ?? [] })).AsTask();
        static byte[] HeaderOnly(Terminal phone)
        {
            byte[] frame = [NccConstants.FrameStart, NccControl.ReTrans, 10, .. Convert.FromHexString(phone.TerminalIdHex), 0, 0, NccConstants.FrameEnd];
            var crc = NccCrc16.Compute(frame.AsSpan(0, frame.Length - 3)); frame[^3] = (byte)crc; frame[^2] = (byte)(crc >> 8); return frame;
        }
    }

    [PostgresIntegrationFact]
    public async Task Immediate_card_and_rate_bundles_acknowledge_only_durable_call_records_across_reconnects()
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
        var prefix = new byte[39]; prefix[0] = 2; prefix[^3] = 0xA5;
        var first = Call(0x5341); var second = Call(0x5342);
        byte[] request = [.. prefix, .. first, .. second];
        Assert.Equal(147, request.Length);
        byte[] receipts = [5, 0x41, 0x53, 5, 0x42, 0x53];
        Assert.True(DlogCardAuthorizationWire.TryBuildDecline(prefix, out var decline));
        int initialPayments;
        await using (var scope = factory.Services.CreateAsyncScope())
            initialPayments = await scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>().PaymentTransactions.CountAsync();

        byte[] expected = [.. decline, .. receipts];
        for (var pass = 0; pass < 2; pass++)
            Assert.Equal(expected, await Exchange(request));
        byte[] rated = [.. DlogRateWire.EncodeRequest("3335550120", false), .. first, .. second];
        var quote = await Exchange(rated);
        Assert.Equal(64, quote[0]); Assert.Equal(receipts, quote[DlogRateWire.ResponseLength..]);

        var attempts = await api.GetFromJsonAsync<JsonElement>($"/api/cards/authorization-attempts?terminalId={phone.Id}");
        Assert.Equal(2, attempts.GetArrayLength());
        Assert.All(attempts.EnumerateArray(), row =>
        {
            Assert.Equal("provider_unavailable", row.GetProperty("outcome").GetString());
            Assert.Equal(147, row.GetProperty("requestBytes").GetInt32());
            Assert.Equal(2, row.GetProperty("bundledCallRecords").GetInt32());
        });
        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        Assert.Equal(initialPayments, await db.PaymentTransactions.CountAsync());
        var cdrs = await db.UploadBatches.Include(row => row.Records).Where(row => row.TerminalId == phone.Id && row.IdempotencyKey.StartsWith(TerminalUploads.CallDetailPrefix)).ToListAsync();
        Assert.Equal(2, cdrs.Count);
        Assert.All(cdrs.SelectMany(row => row.Records), row => Assert.DoesNotContain("ABABAB", row.DecodedMetadataJson, StringComparison.OrdinalIgnoreCase));
        var batch = Assert.Single(await db.DownloadBatches.Where(row => row.TerminalId == phone.Id).ToListAsync());
        Assert.Equal(queuedId, batch.Id); Assert.Equal(DownloadBatchStatus.Pending, batch.Status);
        var sessions = await db.NccSessionFrames.Where(row => row.NccSession!.TerminalId == phone.Id && !row.SentByHost && (row.WireLength == request.Length + 11 || row.WireLength == rated.Length + 11)).ToListAsync();
        Assert.Equal(3, sessions.Count); Assert.All(sessions, row => Assert.Empty(row.RawBytes));

        async Task<byte[]> Exchange(byte[] data)
        {
            using var socket = new TcpClient(); await socket.ConnectAsync(IPAddress.Loopback, port);
            var stream = socket.GetStream();
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = 0, TerminalId = wire, Data = data }));
            AssertAck(await Read(stream)); var response = await Read(stream);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = (byte)(NccControl.Ack | NccControl.Clr | response.Control) }));
            return response.Data;
        }
    }

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
