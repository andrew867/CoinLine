using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

public sealed class NccListenFixture : IAsyncLifetime
{
    public NccFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public int Port { get; private set; }

    public async Task InitializeAsync()
    {
        Factory = new NccFactory();
        Port = Factory.Port;
        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "ui@local");
        Client.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, Port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100);
            }
        }

        throw new TimeoutException("NCC listener did not accept a connection.");
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }
}

public sealed class NccFactory : WebApplicationFactory<Program>
{
    public int Port { get; }
    public AdjustableClock Clock { get; } = new();

    public NccFactory()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint!).Port;
        probe.Stop();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Ncc:Listen", "1");
        builder.UseSetting("Ncc:Port", Port.ToString());
        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    public sealed class AdjustableClock : TimeProvider
    {
        private long _offsetTicks;
        public void Advance(TimeSpan offset) => Interlocked.Exchange(ref _offsetTicks, offset.Ticks);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));
    }
}

public class NccOperationalTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] KnownId = Convert.FromHexString("0102030405");
    private static readonly byte[] UnknownId = Convert.FromHexString("DAD1EAF010");

    [Fact]
    public async Task Known_id_is_stored_and_acked()
    {
        var sent = Message(KnownId, [84, 0x11, 0x22]);
        var link = await OpenAsync();
        await link.Stream.WriteAsync(sent);
        var ack = await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5));
        AssertAck(ack);
        await link.DisposeAsync();

        var session = await WaitSessionAsync(Convert.ToHexString(sent));
        Assert.Equal(1, session.GetProperty("status").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, session.GetProperty("terminalId").ValueKind);
        var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/ncc/sessions/{session.GetProperty("id").GetGuid()}", Json);
        var frames = detail!.GetProperty("frames");
        Assert.Contains(frames.EnumerateArray(), f => f.GetProperty("rawHex").GetString() == Convert.ToHexString(sent));
        Assert.True(frames.EnumerateArray().First().GetProperty("terminalMatched").GetBoolean());

        var events = await EventsAsync(session.GetProperty("correlationId").GetString()!);
        Assert.Contains(events, e => e.GetProperty("action").GetString() == "session_open");
        Assert.Contains(events, e => e.GetProperty("action").GetString() == "frame_stored");
        Assert.Contains(events, e => e.GetProperty("action").GetString() == "session_close");
    }

    [Fact]
    public async Task Unknown_id_is_kept_and_not_dropped()
    {
        var before = await BatchIdsAsync();
        var sent = Message(UnknownId, [84, 0xAB]);
        var link = await OpenAsync();
        await link.Stream.WriteAsync(sent);
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        var extra = false;
        try
        {
            await ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(600));
            extra = true;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.False(extra);
        await link.DisposeAsync();
        var session = await WaitSessionAsync(Convert.ToHexString(sent));
        Assert.Equal(JsonValueKind.Null, session.GetProperty("terminalId").ValueKind);
        var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/ncc/sessions/{session.GetProperty("id").GetGuid()}", Json);
        var frame = detail!.GetProperty("frames").EnumerateArray().Single(f => f.GetProperty("rawHex").GetString() == Convert.ToHexString(sent));
        Assert.False(frame.GetProperty("terminalMatched").GetBoolean());
        Assert.Equal(Convert.ToHexString(UnknownId), frame.GetProperty("terminalIdHex").GetString());
        var events = await EventsAsync(session.GetProperty("correlationId").GetString()!);
        Assert.Contains(events, e =>
            e.GetProperty("action").GetString() == "frame_stored"
            && e.GetProperty("detailJson").GetString()!.Contains("unknown", StringComparison.OrdinalIgnoreCase));
        var after = await BatchIdsAsync();
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public async Task Two_frames_are_both_stored()
    {
        var first = Message(KnownId, [84, 0x31]);
        var second = Message(KnownId, [84, 0x32]);
        var link = await OpenAsync();
        await link.Stream.WriteAsync(first);
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        await link.Stream.WriteAsync(second);
        var sawSecondAck = false;
        for (var i = 0; i < 8 && !sawSecondAck; i++)
        {
            var frame = await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5));
            var parsed = Decode(frame);
            if (NccControl.IsControlPacket(parsed.Control) && (parsed.Control & NccControl.Ack) != 0)
                sawSecondAck = true;
        }

        Assert.True(sawSecondAck);
        await link.DisposeAsync();
        var session = await WaitSessionAsync(Convert.ToHexString(second));
        var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/ncc/sessions/{session.GetProperty("id").GetGuid()}", Json);
        var hexes = detail!.GetProperty("frames").EnumerateArray().Select(f => f.GetProperty("rawHex").GetString()).ToArray();
        Assert.Contains(Convert.ToHexString(first), hexes);
        Assert.Contains(Convert.ToHexString(second), hexes);
        Assert.True(hexes.Length >= 2);
    }

    [Fact]
    public async Task Time_sync_replies_after_link_ack_and_control_frames_keep_the_terminal()
    {
        await using var link = await OpenAsync();
        var requestStarted = Stopwatch.GetTimestamp();
        await link.Stream.WriteAsync(Message(KnownId, [DlogTimeSyncWire.Request]));

        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.True(Stopwatch.GetElapsedTime(requestStarted) >= TimeSpan.FromMilliseconds(300));
        var reply = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(KnownId, reply.TerminalId);
        Assert.Equal(0, reply.Control);
        Assert.Equal(8, reply.Data.Length);
        Assert.Equal(DlogTimeSyncWire.Response, reply.Data[0]);
        var syncTime = new DateTime(1900 + reply.Data[1], reply.Data[2], reply.Data[3],
            reply.Data[4], reply.Data[5], reply.Data[6], DateTimeKind.Utc);
        Assert.Equal((int)syncTime.DayOfWeek + 1, reply.Data[7]);
        Assert.InRange(syncTime, DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow);

        await link.Stream.WriteAsync(Control(NccControl.Nack));
        var replyRetry = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(NccControl.ReTrans, replyRetry.Control);
        Assert.Equal(reply.Data, replyRetry.Data);

        var ack = NccFrameCodec.Encode(new NccWirePacket { Control = NccControl.Ack, TerminalId = KnownId });
        await link.Stream.WriteAsync(ack);
        var end = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(KnownId, end.TerminalId);
        Assert.Equal(1, end.Control);
        Assert.Equal(new byte[] { 13 }, end.Data);

        await link.Stream.WriteAsync(Control(NccControl.Nack | 1));
        var endRetry = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(NccControl.ReTrans | 1, endRetry.Control);
        Assert.Equal(end.Data, endRetry.Data);

        var endAck = NccFrameCodec.Encode(new NccWirePacket { Control = NccControl.Ack | 1, TerminalId = KnownId });
        await link.Stream.WriteAsync(endAck);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(500)));

        var clear = NccFrameCodec.Encode(new NccWirePacket { Control = NccControl.Clr | 3, TerminalId = KnownId });
        await link.Stream.WriteAsync(clear);
        var session = await WaitSessionAsync(Convert.ToHexString(clear));
        Assert.NotEqual(JsonValueKind.Null, session.GetProperty("terminalId").ValueKind);
        var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/ncc/sessions/{session.GetProperty("id").GetGuid()}", Json);
        Assert.Equal(6, detail.GetProperty("frames").GetArrayLength());
        Assert.All(detail.GetProperty("frames").EnumerateArray().Skip(1),
            frame => Assert.Equal(string.Empty, frame.GetProperty("terminalIdHex").GetString()));
    }

    [Fact]
    public async Task Header_only_retransmission_is_reacked_only_for_the_last_bound_message()
    {
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, [DlogTimeSyncWire.Request]));
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(DlogTimeSyncWire.Response, Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5))).Data[0]);

        var invalidCrc = HeaderOnlyRetransmission(KnownId, 0);
        invalidCrc[^3] ^= 1;
        await link.Stream.WriteAsync(invalidCrc);
        await link.Stream.WriteAsync(HeaderOnlyRetransmission(UnknownId, 0));
        await link.Stream.WriteAsync(HeaderOnlyRetransmission(KnownId, 1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(500)));

        await link.Stream.WriteAsync(HeaderOnlyRetransmission(KnownId, 0));
        var ack = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(NccControl.Ack, ack.Control);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Rate_request_returns_type_64_for_allowed_and_denied()
    {
        await AssignRateTableSetAsync("NCC runtime rating test");
        var batches = await BatchIdsAsync();
        var allowed = DlogRateWire.EncodeRequest("5551212", international: true);
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(KnownId, allowed));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var response = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(DlogRateWire.MessageTypeResponse, response.Data[0]);
            Assert.Equal(DlogRateWire.ResponseLength, response.Data.Length);
            Assert.Equal(DlogRateWire.RateFound, response.Data[1]);
            Assert.Equal(2, response.Data[4]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(300)));
        }

        var call = await WaitCallAsync("5551212");
        Assert.Equal(1, call.GetProperty("disposition").GetInt32());
        var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/call-records/{call.GetProperty("id").GetGuid()}", Json);
        Assert.StartsWith("3F", detail!.GetProperty("requestRawHex").GetString());
        Assert.StartsWith("40", detail.GetProperty("responseRawHex").GetString());
        Assert.NotEqual(JsonValueKind.Null, call.GetProperty("terminalId").ValueKind);

        var denied = DlogRateWire.EncodeRequest("9005550100", international: false);
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(KnownId, denied));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var response = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(DlogRateWire.MessageTypeResponse, response.Data[0]);
            Assert.Equal(DlogRateWire.RateBlocked, response.Data[1]);
            Assert.Equal(0, response.Data[4]);
        }

        var blocked = await WaitCallAsync("9005550100");
        Assert.Equal(2, blocked.GetProperty("disposition").GetInt32());

        var empty = DlogRateWire.EncodeRequest("", false);
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(KnownId, empty));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var response = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(DlogRateWire.MessageTypeResponse, response.Data[0]);
            Assert.Equal(DlogRateWire.ResponseLength, response.Data.Length);
            Assert.Equal(DlogRateWire.RateBlocked, response.Data[1]);
        }

        var blank = await WaitCallAsync("");
        Assert.Equal("", blank.GetProperty("dialedDigits").GetString());
        Assert.True(batches.SetEquals(await BatchIdsAsync()));
    }

    [Theory]
    [InlineData(1, 29, 3, 2)]
    [InlineData(2, 39, 65, 22)]
    public async Task Card_authorization_returns_only_decline_and_keeps_credentials_out_of_session(
        byte requestType, int requestLength, byte replyType, int replyLength)
    {
        await AssignRateTableSetAsync($"NCC card authorization test {requestType}");
        var batches = await BatchIdsAsync();
        var request = new byte[requestLength];
        request[0] = requestType;
        request[^3] = 0x5A;
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, request));
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        var reply = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        var expected = new byte[replyLength];
        expected[0] = replyType;
        expected[1] = 1;
        Assert.Equal(expected, reply.Data);
        Assert.Equal(KnownId, reply.TerminalId);

        var sessions = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/ncc/sessions", Json))!;
        var session = sessions.EnumerateArray().First(row => row.GetProperty("socketBound").GetBoolean());
        Assert.Equal("", session.GetProperty("lastFrameSampleHex").GetString());
        var detail = (await fx.Client.GetFromJsonAsync<JsonElement>($"/api/ncc/sessions/{session.GetProperty("id").GetGuid()}", Json))!;
        Assert.Equal("", detail.GetProperty("frames")[0].GetProperty("rawHex").GetString());

        await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | reply.Control)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(400)));
        await link.Stream.WriteAsync(Control(NccControl.Clr));
        Assert.True(batches.SetEquals(await BatchIdsAsync()));
    }

    [Fact]
    public async Task Rate_request_uses_bcd_number_not_ascii_run()
    {
        var noise = "9999999999"u8.ToArray();
        var body = DlogRateWire.EncodeRequest("5551212", false, noise);
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, body));
        await ReadUntilAsync(link.Stream, p => p.Data.Length > 0 && p.Data[0] == DlogRateWire.MessageTypeResponse);
        var call = await WaitCallAsync("5551212");
        Assert.Equal("5551212", call.GetProperty("dialedDigits").GetString());
        var calls = await fx.Client.GetFromJsonAsync<JsonElement>("/api/call-records", Json);
        Assert.DoesNotContain(calls!.EnumerateArray(), c => c.GetProperty("dialedDigits").GetString() == "9999999999");
    }

    [Fact]
    public async Task Unhandled_message_and_unsolicited_table_ack_do_not_start_install()
    {
        await AssignRateTableSetAsync("NCC unsolicited record test");
        var before = await BatchIdsAsync();
        await using var link = await OpenAsync();
        foreach (var payload in new byte[][] { [84], [1, 0x71], [4, 0x53], [14, 73] })
        {
            await link.Stream.WriteAsync(Message(KnownId, payload));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(400)));
        Assert.True(before.SetEquals(await BatchIdsAsync()));
    }

    [Fact]
    public async Task Terminal_call_uploads_are_saved_before_the_receipts_and_deduplicated_after_reconnect()
    {
        var tableBatches = await BatchIdsAsync();
        var existing = await fx.Client.GetFromJsonAsync<JsonElement>("/api/uploads", Json);
        var before = existing.EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToHashSet();
        var card = TerminalCallDetail(4, 0x1248, 0x44);
        var coin = TerminalCallDetail(61, 0x1249, 0x33);
        var combined = TerminalCallDetail(53, 0x124A, 0x65);
        byte[] bundle = [.. card, .. coin, .. combined, 13];
        byte[] receipts = [5, 0x48, 0x12, 5, 0x49, 0x12, 5, 0x4A, 0x12];
        await using (var upload = await OpenAsync())
        {
            await upload.Stream.WriteAsync(Message(KnownId, [42, 0]));
            AssertAck(await ReadFrameAsync(upload.Stream, TimeSpan.FromSeconds(5)));
            var request = Decode(await ReadFrameAsync(upload.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 17 }, request.Data);
            await upload.Stream.WriteAsync(Control((byte)(NccControl.Ack | request.Control)));

            await upload.Stream.WriteAsync(Message(KnownId, bundle));
            AssertAck(await ReadFrameAsync(upload.Stream, TimeSpan.FromSeconds(5)));
            var actual = Decode(await ReadFrameAsync(upload.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(receipts, actual.Data);
            var stored = await fx.Client.GetFromJsonAsync<JsonElement>("/api/uploads", Json);
            var additions = stored.EnumerateArray().Where(row => !before.Contains(row.GetProperty("id").GetGuid())).ToArray();
            Assert.Equal(3, additions.Length);
            var uploaded = new Dictionary<int, JsonElement>();
            foreach (var row in additions)
            {
                Assert.True(row.GetProperty("isTerminalCallDetail").GetBoolean());
                Assert.Equal("", row.GetProperty("rawPayloadHex").GetString());
                Assert.Equal(2, row.GetProperty("status").GetInt32());
                var path = $"/api/uploads/{row.GetProperty("id").GetGuid()}";
                var detail = await fx.Client.GetFromJsonAsync<JsonElement>(path, Json);
                var record = Assert.Single(detail.GetProperty("records").EnumerateArray());
                Assert.Equal("", record.GetProperty("rawPayloadHex").GetString());
                Assert.DoesNotContain("ABABAB", detail.GetRawText(), StringComparison.OrdinalIgnoreCase);
                var call = record.GetProperty("terminalCall");
                uploaded.Add(call.GetProperty("messageType").GetInt32(), call);
                using var reprocess = await fx.Client.PostAsync($"{path}/reprocess?confirm=true", null);
                Assert.Equal(HttpStatusCode.Conflict, reprocess.StatusCode);
            }
            Assert.Equal("Credit card", uploaded[4].GetProperty("payment").GetString());
            Assert.Equal(JsonValueKind.Null, uploaded[4].GetProperty("amountCharged").ValueKind);
            Assert.Equal("Coins", uploaded[61].GetProperty("payment").GetString());
            Assert.Equal(80, uploaded[61].GetProperty("amountCollected").GetInt32());
            Assert.Equal("5550196", uploaded[53].GetProperty("dialedDigits").GetString());
            Assert.Equal("2028-02-29T10:11:12", uploaded[53].GetProperty("terminalStart").GetString());
            Assert.Equal(65, uploaded[53].GetProperty("durationSeconds").GetInt32());
            Assert.Equal(25, uploaded[53].GetProperty("amountCharged").GetInt32());
            var sessions = await fx.Client.GetFromJsonAsync<JsonElement>("/api/ncc/sessions", Json);
            var id = sessions.EnumerateArray().First(row => row.GetProperty("socketBound").GetBoolean()).GetProperty("id").GetGuid();
            var session = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/ncc/sessions/{id}", Json);
            Assert.Equal("", session.GetProperty("frames").EnumerateArray().Last().GetProperty("rawHex").GetString());
            await upload.Stream.WriteAsync(HeaderOnlyRetransmission(KnownId, 0));
            AssertAck(await ReadFrameAsync(upload.Stream, TimeSpan.FromSeconds(5)));
            var repeated = Decode(await ReadFrameAsync(upload.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(actual.Control | NccControl.ReTrans, repeated.Control);
            Assert.Equal(receipts, repeated.Data);
            await upload.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | actual.Control)));
        }

        await using (var retry = await OpenAsync())
        {
            await retry.Stream.WriteAsync(Message(KnownId, bundle));
            AssertAck(await ReadFrameAsync(retry.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(receipts, Decode(await ReadFrameAsync(retry.Stream, TimeSpan.FromSeconds(5))).Data);
        }
        await using (var truncated = await OpenAsync())
        {
            await truncated.Stream.WriteAsync(Message(KnownId, combined[..^1]));
            AssertAck(await ReadFrameAsync(truncated.Stream, TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ReadFrameAsync(truncated.Stream, TimeSpan.FromMilliseconds(400)));
        }
        var after = await fx.Client.GetFromJsonAsync<JsonElement>("/api/uploads", Json);
        Assert.Equal(before.Count + 3, after.GetArrayLength());
        Assert.True(tableBatches.SetEquals(await BatchIdsAsync()));
    }

    [Fact]
    public async Task Terminal_statistics_are_decoded_and_saved_before_link_acknowledgement()
    {
        var tablesBefore = await BatchIdsAsync();
        var uploadsBefore = await fx.Client.GetFromJsonAsync<JsonElement>("/api/uploads", Json);
        var existing = uploadsBefore.EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToHashSet();
        var calls = StatisticsReport(56, 81);
        BinaryPrimitives.WriteUInt16LittleEndian(calls.AsSpan(35), 22);
        BinaryPrimitives.WriteUInt32LittleEndian(calls.AsSpan(65), 7200);
        var performance = StatisticsReport(37, 99);
        BinaryPrimitives.WriteUInt16LittleEndian(performance.AsSpan(21), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(performance.AsSpan(77), 9);
        var carriers = StatisticsReport(57, 190);
        carriers[13] = 0;
        carriers[72] = 8;
        carriers[131] = 0xFE;
        BinaryPrimitives.WriteUInt16LittleEndian(carriers.AsSpan(73), 4);
        var extended = StatisticsReport(71, 244);
        extended[13] = 2;
        extended[14] = 9;
        extended[129] = 0xFE;
        BinaryPrimitives.WriteUInt16LittleEndian(extended.AsSpan(21), 11);
        var messaging = StatisticsReport(69, 213);
        BinaryPrimitives.WriteUInt16LittleEndian(messaging.AsSpan(77), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(messaging.AsSpan(91), 5);
        var status = new byte[11];
        status[0] = 10;
        status[8] = 0x02;
        var version = new byte[28];
        version[0] = 60;
        "V100001"u8.CopyTo(version.AsSpan(1));
        "90A "u8.CopyTo(version.AsSpan(8));
        "20B "u8.CopyTo(version.AsSpan(19));
        version[23] = 2;
        var cash = new byte[57];
        cash[0] = 38;

        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(KnownId, [42, 8, .. status, .. version]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var invitation = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 17 }, invitation.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | invitation.Control)));
            var ledger = await fx.Client.GetFromJsonAsync<JsonElement>("/api/field-events", Json);
            var events = ledger.GetProperty("items").EnumerateArray().ToList();
            Assert.Contains(events, row => row.GetProperty("summary").GetString() == "Statistics checksum fault");
            Assert.Contains(events, row => row.GetProperty("summary").GetString() ==
                "Controller ROM V100001, Controller software 90A, Telephony software 20B, Operating mode Multi-pay (cards and coins)");

            await link.Stream.WriteAsync(Message(KnownId, [.. TerminalCallDetail(61, 0x7675, 0x33), .. status, .. version, .. cash, .. calls]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var receipt = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 5, 0x75, 0x76 }, receipt.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | receipt.Control)));

            foreach (var data in new byte[][] { carriers, extended, messaging, [.. performance, 13] })
            {
                await link.Stream.WriteAsync(Message(KnownId, data));
                AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            }

            await link.Stream.WriteAsync(HeaderOnlyRetransmission(KnownId, 0));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(400)));
        }

        await using (var replay = await OpenAsync())
        {
            // Empty slots can contain stale padding bytes that change between sends.
            extended[^1] = 0xAA;
            await replay.Stream.WriteAsync(Message(KnownId, extended));
            AssertAck(await ReadFrameAsync(replay.Stream, TimeSpan.FromSeconds(5)));
        }

        var uploads = await fx.Client.GetFromJsonAsync<JsonElement>("/api/uploads", Json);
        var reports = new Dictionary<int, JsonElement>();
        foreach (var row in uploads.EnumerateArray().Where(row => !existing.Contains(row.GetProperty("id").GetGuid()) &&
            row.GetProperty("isTerminalStatistics").GetBoolean()))
        {
            var path = $"/api/uploads/{row.GetProperty("id").GetGuid()}";
            var detail = await fx.Client.GetFromJsonAsync<JsonElement>(path, Json);
            var record = Assert.Single(detail.GetProperty("records").EnumerateArray());
            Assert.Equal("", record.GetProperty("rawPayloadHex").GetString());
            var report = record.GetProperty("terminalStatistics");
            Assert.Equal("2028-02-29T10:11:12", report.GetProperty("periodStart").GetString());
            reports.Add(report.GetProperty("messageType").GetInt32(), report);
            using var reprocess = await fx.Client.PostAsync($"{path}/reprocess?confirm=true", null);
            Assert.Equal(HttpStatusCode.Conflict, reprocess.StatusCode);
        }
        Assert.Equal(5, reports.Count);
        Assert.Equal(22, StatisticsCounter(reports[56], "Call counters", "Directory assistance"));
        Assert.Equal(7200, StatisticsCounter(reports[56], "Use duration", "Completed calls (seconds)"));
        Assert.Equal(8, StatisticsCounter(reports[37], "Line seizure to end of dialing", "Bucket 1"));
        Assert.Equal(9, StatisticsCounter(reports[37], "Connection counters", "Failed dialogues"));
        var carrierRows = reports[57].GetProperty("carriers");
        Assert.Equal(2, carrierRows.GetArrayLength());
        Assert.Equal(0, carrierRows[0].GetProperty("reference").GetInt32());
        Assert.Equal(4, StatisticsCounter(carrierRows[1], "Local", "Coins"));
        Assert.Equal(2, reports[71].GetProperty("carrierFormat").GetInt32());
        Assert.Equal(11, StatisticsCounter(Assert.Single(reports[71].GetProperty("carriers").EnumerateArray()), "Local", "Mondex"));
        Assert.Equal(3, StatisticsCounter(reports[69], "Stored-value cards", "Local"));
        Assert.Equal(5, StatisticsCounter(reports[69], "Other messaging counters", "No-answer counter"));
        Assert.True(tablesBefore.SetEquals(await BatchIdsAsync()));
    }

    [Fact]
    public async Task Truncated_statistics_close_the_link_before_acknowledgement()
    {
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, StatisticsReport(37, 99)[..^1]));
        await Assert.ThrowsAsync<EndOfStreamException>(
            () => ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
    }

    private static byte[] StatisticsReport(byte type, int length)
    {
        var report = new byte[length];
        report[0] = type;
        new byte[] { 128, 2, 29, 10, 11, 12, 128, 3, 1, 10, 11, 12 }.CopyTo(report, 1);
        return report;
    }

    private static int StatisticsCounter(JsonElement report, string group, string label) => report.GetProperty("groups").EnumerateArray()
        .Single(element => element.GetProperty("label").GetString() == group).GetProperty("counters").EnumerateArray()
        .Single(element => element.GetProperty("label").GetString() == label).GetProperty("value").GetInt32();

    [Fact]
    public async Task Rating_with_a_call_detail_suffix_keeps_the_suffix_out_of_captures()
    {
        var request = DlogRateWire.EncodeRequest("5550187", international: false);
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, [.. request, .. TerminalCallDetail(53, 0xF005, 0x45)]));
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(64, Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5))).Data[0]);
        var call = await WaitCallAsync("5550187");
        var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/call-records/{call.GetProperty("id").GetGuid()}", Json);
        Assert.Equal(Convert.ToHexString(request), detail.GetProperty("requestRawHex").GetString());
        var sessions = await fx.Client.GetFromJsonAsync<JsonElement>("/api/ncc/sessions", Json);
        var session = sessions.EnumerateArray().First(row => row.GetProperty("socketBound").GetBoolean());
        Assert.Equal("", session.GetProperty("lastFrameSampleHex").GetString());
    }

    private static byte[] TerminalCallDetail(byte type, ushort sequence, byte classification)
    {
        var combined = type == 53;
        var record = new byte[combined ? 54 : 35];
        record[0] = type;
        var digits = DlogRateWire.EncodeRequest("5550196", international: false);
        digits.AsSpan(3, 10).CopyTo(record.AsSpan(2));
        if (type != 61)
            record.AsSpan(13, 10).Fill(0xAB);
        if (combined)
            record.AsSpan(43, 8).Fill(0xAB);
        if (type != 4)
        {
            var amountOffset = combined ? 23 : 13;
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(amountOffset), 80);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(amountOffset + 4), 25);
        }
        var offset = combined ? 31 : 23;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(offset), sequence);
        byte[] time = [128, 2, 29, 10, 11, 12, 0, 1, 5, classification];
        time.CopyTo(record, offset + 2);
        return record;
    }

    [Fact]
    public async Task Native_feature_edit_publishes_and_transfers_exact_changed_bytes()
    {
        var definitions = await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/definitions", Json);
        var featureDefinition = definitions.EnumerateArray().Single(d => d.GetProperty("tableNumber").GetInt32() == 26);
        using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = "Native follow-on test", isDefault = false }, Json);
        created.EnsureSuccessStatusCode();
        var setId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var bytes = new byte[72];
        bytes[0] = 26;
        bytes[1] = 1;
        bytes[22] = 0x80;
        using var added = await fx.Client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = setId, tableDefinitionId = featureDefinition.GetProperty("id").GetGuid(),
            tableRevision = 1, payloadBase64 = Convert.ToBase64String(bytes), sortOrder = 0
        }, Json);
        added.EnsureSuccessStatusCode();

        var fields = $"/api/tables/sets/{setId}/records/26/fields";
        var original = await fx.Client.GetFromJsonAsync<JsonElement>(fields, Json);
        Assert.True(original.GetProperty("supported").GetBoolean());
        Assert.True(original.GetProperty("editable").GetBoolean());
        var edits = new { revision = 1, sha256 = original.GetProperty("sha256").GetString(),
            changes = new Dictionary<string, string> { ["terminalType"] = "2", ["onHookAds"] = "true" } };
        using var saved = await fx.Client.PutAsJsonAsync(fields, edits, Json);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var stale = await fx.Client.PutAsJsonAsync(fields, edits, Json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var published = await fx.Client.PostAsync($"/api/tables/sets/{setId}/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
        using var immutable = await fx.Client.PutAsJsonAsync(fields, edits, Json);
        Assert.Equal(HttpStatusCode.Conflict, immutable.StatusCode);

        await AssignSetAsync(setId);
        try
        {
            var before = await BatchIdsAsync();
            await using var link = await OpenAsync();
            await link.Stream.WriteAsync(Message(KnownId, [44, 2]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var header = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 18 }, header.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
            var record = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            bytes[1] = 2;
            bytes[22] = 0x81;
            Assert.Equal(bytes, record.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | record.Control)));
            await link.Stream.WriteAsync(Message(KnownId, [14, 26]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var end = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, end.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
            await WaitNewBatchAsync(before, 2);
        }
        finally
        {
            await AssignLabSetAsync();
        }
    }

    [Fact]
    public async Task Firmware_target_selects_wire_layout_and_blocks_incompatible_assignments_and_pickup()
    {
        var terminals = await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json);
        var terminal = terminals.EnumerateArray()
            .Single(t => t.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId));
        var terminalId = terminal.GetProperty("id").GetGuid();
        var originalFirmware = terminal.GetProperty("firmwareVersionId").GetGuid();
        var phone = $"/api/terminals/{terminalId}";
        using var registered = await fx.Client.PostAsJsonAsync("/api/firmware/versions", new
        {
            label = "MTR 1.9 Multi-pay fixture", tableProfileId = "mtr19-multipay-200"
        }, Json);
        registered.EnsureSuccessStatusCode();
        var firmware = (await registered.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = "Firmware compatibility test" }, Json);
        created.EnsureSuccessStatusCode();
        var set = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{set}";
        using var targeted = await fx.Client.PutAsJsonAsync($"{path}/firmware-target", new { firmwareVersionId = firmware }, Json);
        Assert.Equal(HttpStatusCode.NoContent, targeted.StatusCode);

        var definitions = await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/definitions", Json);
        var feature = definitions.EnumerateArray().Single(d => d.GetProperty("tableNumber").GetInt32() == 26).GetProperty("id").GetGuid();
        using var smart = await fx.Client.PostAsJsonAsync("/api/tables/definitions", new { name = "Smart-card profile test", tableNumber = 93 }, Json);
        smart.EnsureSuccessStatusCode();
        var smartDefinition = (await smart.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        async Task SaveRecord(Guid definition, byte[] record, int revision, int sortOrder)
        {
            using var result = await fx.Client.PostAsJsonAsync("/api/tables/versions", new
            {
                tableSetId = set, tableDefinitionId = definition, tableRevision = revision,
                payloadBase64 = Convert.ToBase64String(record), sortOrder
            }, Json);
            result.EnsureSuccessStatusCode();
        }

        await SaveRecord(feature, [26, 2, 3], 1, 0);
        using var invalid = await fx.Client.PostAsync($"{path}/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var issue = (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json)).GetProperty("firmwareFindings")[0];
        Assert.True(issue.GetProperty("blocking").GetBoolean());
        Assert.Contains("72 bytes", issue.GetProperty("message").GetString());

        var featureBytes = new byte[72];
        featureBytes[0] = 26;
        featureBytes[1] = 2;
        await SaveRecord(feature, featureBytes, 2, 0);
        var smartBytes = new byte[225];
        smartBytes[0] = 93;
        await SaveRecord(smartDefinition, smartBytes, 1, 1);
        var fields = await fx.Client.GetFromJsonAsync<JsonElement>($"{path}/records/93/fields", Json);
        Assert.Equal("MTR 1.9 packed", fields.GetProperty("profile").GetString());
        var wrongLayout = await fx.Client.GetFromJsonAsync<JsonElement>($"{path}/records/93/fields?profile=Later%20packed%20profile", Json);
        Assert.False(wrongLayout.GetProperty("supported").GetBoolean());

        using var publish = await fx.Client.PostAsync($"{path}/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);
        using var clone = await fx.Client.PostAsJsonAsync($"{path}/clone", new { name = "Firmware target clone test" }, Json);
        clone.EnsureSuccessStatusCode();
        var copy = (await clone.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var cloned = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{copy}", Json);
        Assert.Equal(firmware, cloned.GetProperty("targetFirmwareVersionId").GetGuid());

        var selection = new { targetType = "terminal", targetId = terminalId, terminalIds = new[] { terminalId } };
        var preview = $"{path}/assignment-preview?targetType=terminal&targetId={terminalId}";
        var before = await BatchIdsAsync();
        try
        {
            using var unidentified = await fx.Client.PutAsJsonAsync($"{phone}/firmware", new { firmwareVersionId = (Guid?)null }, Json);
            Assert.Equal(HttpStatusCode.NoContent, unidentified.StatusCode);
            var unknown = (await fx.Client.GetFromJsonAsync<JsonElement>(preview, Json)).GetProperty("targets")[0];
            Assert.Contains("unknown", unknown.GetProperty("reason").GetString()!);
            using var wrong = await fx.Client.PutAsJsonAsync($"{phone}/firmware", new { firmwareVersionId = originalFirmware }, Json);
            Assert.Equal(HttpStatusCode.NoContent, wrong.StatusCode);
            var mismatch = (await fx.Client.GetFromJsonAsync<JsonElement>(preview, Json)).GetProperty("targets")[0];
            Assert.False(mismatch.GetProperty("eligible").GetBoolean());
            using var direct = await fx.Client.PostAsJsonAsync($"{phone}/table-assignment", new { tableSetId = set }, Json);
            Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);
            using var forbiddenBatch = await fx.Client.PostAsJsonAsync($"{phone}/downloads", new { tableSetId = set, scope = "Full" }, Json);
            Assert.Equal(HttpStatusCode.BadRequest, forbiddenBatch.StatusCode);
            using var forbiddenSchedule = await fx.Client.PostAsJsonAsync($"{path}/schedules", selection, Json);
            Assert.Equal(HttpStatusCode.Conflict, forbiddenSchedule.StatusCode);

            using var enrollment = await fx.Client.PutAsJsonAsync($"{phone}/firmware", new { firmwareVersionId = firmware }, Json);
            Assert.Equal(HttpStatusCode.NoContent, enrollment.StatusCode);
            var compatible = (await fx.Client.GetFromJsonAsync<JsonElement>(preview, Json)).GetProperty("targets")[0];
            Assert.True(compatible.GetProperty("eligible").GetBoolean());
            using var assign = await fx.Client.PostAsJsonAsync($"{path}/assignments", selection, Json);
            assign.EnsureSuccessStatusCode();
            using var schedule = await fx.Client.PostAsJsonAsync($"{path}/schedules", selection, Json);
            schedule.EnsureSuccessStatusCode();

            using var changed = await fx.Client.PutAsJsonAsync($"{phone}/firmware", new { firmwareVersionId = originalFirmware }, Json);
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
            foreach (var query in new byte[][] { [8], [44, 2] })
            {
                await using var refused = await OpenAsync();
                await refused.Stream.WriteAsync(Message(KnownId, query));
                AssertAck(await ReadFrameAsync(refused.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 13 }, Decode(await ReadFrameAsync(refused.Stream, TimeSpan.FromSeconds(5))).Data);
            }
            var batchSchedule = (await fx.Client.GetFromJsonAsync<JsonElement>($"{path}/schedules", Json))[0];
            Assert.Equal("Cancelled", batchSchedule.GetProperty("status").GetString());
            Assert.True(before.SetEquals(await BatchIdsAsync()));

            using var restored = await fx.Client.PutAsJsonAsync($"{phone}/firmware", new { firmwareVersionId = firmware }, Json);
            Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
            await using var accepted = await OpenAsync();
            await accepted.Stream.WriteAsync(Message(KnownId, [44, 2]));
            AssertAck(await ReadFrameAsync(accepted.Stream, TimeSpan.FromSeconds(5)));
            var header = Decode(await ReadFrameAsync(accepted.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 18 }, header.Data);
            await accepted.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
            foreach (var expected in new[] { featureBytes, smartBytes })
            {
                var record = Decode(await ReadFrameAsync(accepted.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(expected, record.Data);
                await accepted.Stream.WriteAsync(Control((byte)(NccControl.Ack | record.Control)));
                await accepted.Stream.WriteAsync(Message(KnownId, [14, expected[0]]));
                AssertAck(await ReadFrameAsync(accepted.Stream, TimeSpan.FromSeconds(5)));
            }
            var end = Decode(await ReadFrameAsync(accepted.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, end.Data);
            await accepted.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
            await WaitNewBatchAsync(before, 2);
        }
        finally
        {
            using var reset = await fx.Client.PutAsJsonAsync($"{phone}/firmware", new { firmwareVersionId = originalFirmware }, Json);
            await AssignLabSetAsync();
        }
    }

    [Fact]
    public async Task Edited_call_in_parameters_are_sent_in_the_phone_data_set()
    {
        var terminals = await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json);
        var terminalId = terminals.EnumerateArray().Single(item => item.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId))
            .GetProperty("id").GetGuid();
        using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = "Call-in parameters test" }, Json);
        created.EnsureSuccessStatusCode();
        var setId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var setPath = $"/api/tables/sets/{setId}";
        var fieldsPath = $"{setPath}/records/35/fields";
        var missing = await fx.Client.GetFromJsonAsync<JsonElement>(fieldsPath, Json);
        using var initialized = await fx.Client.PostAsJsonAsync(fieldsPath, missing.GetProperty("templates")[0], Json);
        Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
        var fields = await fx.Client.GetFromJsonAsync<JsonElement>(fieldsPath, Json);
        using var saved = await fx.Client.PutAsJsonAsync(fieldsPath, new
        {
            profile = fields.GetProperty("profile"), revision = fields.GetProperty("revision"), sha256 = fields.GetProperty("sha256"),
            changes = new Dictionary<string, string> { ["startTime"] = "21:03", ["retryMinutes"] = "7", ["adjustAt"] = "2028-02-29T03:05", ["adjustment"] = "-3600" }
        }, Json);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var published = await fx.Client.PostAsync($"{setPath}/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);

        try
        {
            using var assigned = await fx.Client.PostAsJsonAsync($"/api/terminals/{terminalId}/table-assignment", new { tableSetId = setId }, Json);
            assigned.EnsureSuccessStatusCode();
            var before = await BatchIdsAsync();
            await using var install = await OpenAsync();
            await install.Stream.WriteAsync(Message(KnownId, [44, 2]));
            AssertAck(await ReadFrameAsync(install.Stream, TimeSpan.FromSeconds(5)));
            var header = Decode(await ReadFrameAsync(install.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 18 }, header.Data);
            await install.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
            var parameters = Decode(await ReadFrameAsync(install.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 35, 0, 0, 0, 21, 3, 0, 1, 0, 0, 7, 0, 30, 128, 2, 29, 3, 5, 0, 0xF0, 0xF1 }, parameters.Data);
            await install.Stream.WriteAsync(Control((byte)(NccControl.Ack | parameters.Control)));
            await install.Stream.WriteAsync(Message(KnownId, [14, 35]));
            AssertAck(await ReadFrameAsync(install.Stream, TimeSpan.FromSeconds(5)));
            var end = Decode(await ReadFrameAsync(install.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, end.Data);
            await install.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
            await WaitNewBatchAsync(before, 2);
        }
        finally
        {
            await AssignLabSetAsync();
        }
    }

    [Fact]
    public async Task Firmware_parameters_schedule_delivers_mt94_on_call_in_and_keeps_the_table_assignment()
    {
        await AssignRateTableSetAsync("Firmware parameter baseline data set");
        var terminals = await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json);
        var terminalId = terminals.EnumerateArray().Single(t => t.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId))
            .GetProperty("id").GetGuid();
        var assignmentPath = $"/api/terminals/{terminalId}/table-assignment";
        var originalAssignment = (await fx.Client.GetFromJsonAsync<JsonElement>(assignmentPath, Json)).GetProperty("tableSetId").GetGuid();
        using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = "Firmware parameter schedule test" }, Json);
        created.EnsureSuccessStatusCode();
        var setId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var setPath = $"/api/tables/sets/{setId}";
        var fieldsPath = $"{setPath}/records/94/fields";
        var missing = await fx.Client.GetFromJsonAsync<JsonElement>(fieldsPath, Json);
        using var initialized = await fx.Client.PostAsJsonAsync(fieldsPath, missing.GetProperty("templates")[0], Json);
        Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
        var fields = await fx.Client.GetFromJsonAsync<JsonElement>(fieldsPath, Json);
        var start = fx.Factory.Clock.GetUtcNow().AddHours(1);
        var settings = new Dictionary<string, string>
        {
            ["enabled"] = "true", ["startAt"] = start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["server"] = "1234", ["targetRom"] = "ROMTEST", ["targetApp"] = "A001", ["sector-0"] = "true",
            ["serverAuthentication"] = Convert.ToHexString(Enumerable.Range(0, 6).Select(index => (byte)(0xA0 + index)).ToArray())
        };
        using var saved = await fx.Client.PutAsJsonAsync(fieldsPath, new
        {
            profile = fields.GetProperty("profile"), revision = fields.GetProperty("revision"), sha256 = fields.GetProperty("sha256"), changes = settings
        }, Json);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var updated = await fx.Client.GetFromJsonAsync<JsonElement>(fieldsPath, Json);
        var authentication = updated.GetProperty("sections")[0].GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("key").GetString() == "serverAuthentication");
        Assert.Equal(JsonValueKind.Null, authentication.GetProperty("value").ValueKind);
        using var published = await fx.Client.PostAsync($"{setPath}/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
        var selection = new { targetType = "terminal", targetId = terminalId, terminalIds = new[] { terminalId } };
        using var assignment = await fx.Client.PostAsJsonAsync($"{setPath}/assignments", selection, Json);
        Assert.Equal(HttpStatusCode.BadRequest, assignment.StatusCode);
        using var direct = await fx.Client.PostAsJsonAsync(assignmentPath, new { tableSetId = setId }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);
        var schedulePath = $"{setPath}/schedules";
        using var late = await fx.Client.PostAsJsonAsync(schedulePath,
            new { selection.targetType, selection.targetId, selection.terminalIds, availableAtUtc = start.AddMinutes(1) }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        using var scheduled = await fx.Client.PostAsJsonAsync(schedulePath, selection, Json);
        scheduled.EnsureSuccessStatusCode();
        var scheduleId = (await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json))[0].GetProperty("id").GetGuid();
        try
        {
            var before = await BatchIdsAsync();
            await using (var install = await OpenAsync())
            {
                await install.Stream.WriteAsync(Message(KnownId, [44, 2]));
                AssertAck(await ReadFrameAsync(install.Stream, TimeSpan.FromSeconds(5)));
                var header = Decode(await ReadFrameAsync(install.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 18 }, header.Data);
                await install.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
                Assert.Equal(73, Decode(await ReadFrameAsync(install.Stream, TimeSpan.FromSeconds(5))).Data[0]);
            }
            await WaitNewBatchAsync(before, 3);
            Assert.Equal("Ready", (await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json))[0].GetProperty("status").GetString());

            var template = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(94)));
            var expected = NativeTableLayouts.Find(94, template)!.Apply(template, settings);
            before = await BatchIdsAsync();
            await using (var callIn = await OpenAsync())
            {
                await callIn.Stream.WriteAsync(Message(KnownId, [8]));
                AssertAck(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
                var header = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 18 }, header.Data);
                await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
                var parameters = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(expected, parameters.Data);
                await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | parameters.Control)));
                await callIn.Stream.WriteAsync(Message(KnownId, [14, 94]));
                AssertAck(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
                var end = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 13 }, end.Data);
                await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
                await WaitNewBatchAsync(before, 2);
            }
            Assert.Equal("Delivered", (await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json))[0].GetProperty("status").GetString());
            Assert.Equal(originalAssignment, (await fx.Client.GetFromJsonAsync<JsonElement>(assignmentPath, Json)).GetProperty("tableSetId").GetGuid());

            using var rescheduled = await fx.Client.PostAsJsonAsync(schedulePath, selection, Json);
            rescheduled.EnsureSuccessStatusCode();
            scheduleId = (await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json))[0].GetProperty("id").GetGuid();
            fx.Factory.Clock.Advance(TimeSpan.FromHours(2));
            before = await BatchIdsAsync();
            await using (var expired = await OpenAsync())
            {
                await expired.Stream.WriteAsync(Message(KnownId, [8]));
                AssertAck(await ReadFrameAsync(expired.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 13 }, Decode(await ReadFrameAsync(expired.Stream, TimeSpan.FromSeconds(5))).Data);
            }
            Assert.True(before.SetEquals(await BatchIdsAsync()));
            var expiredSchedule = (await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json)).EnumerateArray()
                .Single(row => row.GetProperty("id").GetGuid() == scheduleId);
            Assert.Equal("Cancelled", expiredSchedule.GetProperty("status").GetString());
        }
        finally
        {
            fx.Factory.Clock.Advance(TimeSpan.Zero);
            using var cleanup = await fx.Client.PostAsync($"{schedulePath}/{scheduleId}/cancel", null);
            await AssignLabSetAsync();
        }
    }

    [Fact]
    public async Task Scheduled_tables_wait_for_due_call_in_retry_on_failure_and_update_assignment_after_ack()
    {
        await AssignLabSetAsync();
        var payload = new byte[1192];
        payload[^1] = 0x55;
        var setId = await CreateRateTableSetAsync("Scheduled call-in test", payload);
        var terminals = await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json);
        var terminalId = terminals.EnumerateArray()
            .Single(t => t.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId)).GetProperty("id").GetGuid();
        var oldAssignment = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/terminals/{terminalId}/table-assignment", Json);
        var previousSetId = oldAssignment.GetProperty("tableSetId").GetGuid();
        var path = $"/api/tables/sets/{setId}/schedules";
        var body = new
        {
            targetType = "terminal", targetId = terminalId, terminalIds = new[] { terminalId },
            availableAtUtc = fx.Factory.Clock.GetUtcNow().AddHours(1)
        };
        using var queued = await fx.Client.PostAsJsonAsync(path, body, Json);
        queued.EnsureSuccessStatusCode();
        Assert.Equal(1, (await queued.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("scheduled").GetInt32());
        using var duplicate = await fx.Client.PostAsJsonAsync(path, body, Json);
        duplicate.EnsureSuccessStatusCode();
        Assert.Equal(0, (await duplicate.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("scheduled").GetInt32());
        var schedule = (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json))[0];
        Assert.Equal("Scheduled", schedule.GetProperty("status").GetString());
        var scheduleId = schedule.GetProperty("id").GetGuid();
        var before = await BatchIdsAsync();

        try
        {
            await using (var early = await OpenAsync())
            {
                await early.Stream.WriteAsync(Message(KnownId, [8]));
                AssertAck(await ReadFrameAsync(early.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 13 }, Decode(await ReadFrameAsync(early.Stream, TimeSpan.FromSeconds(5))).Data);
            }
            Assert.True(before.SetEquals(await BatchIdsAsync()));
            fx.Factory.Clock.Advance(TimeSpan.FromHours(2));

            // A due download must not appear on a rating or card authorization call.
            foreach (var request in new[]
                { (DlogRateWire.EncodeRequest("5551212", false), (byte)64), (new byte[29], (byte)3) })
            {
                if (request.Item2 == 3)
                    request.Item1[0] = 1;
                await using var runtime = await OpenAsync();
                await runtime.Stream.WriteAsync(Message(KnownId, request.Item1));
                AssertAck(await ReadFrameAsync(runtime.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(request.Item2, Decode(await ReadFrameAsync(runtime.Stream, TimeSpan.FromSeconds(5))).Data[0]);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => ReadFrameAsync(runtime.Stream, TimeSpan.FromMilliseconds(350)));
            }
            Assert.True(before.SetEquals(await BatchIdsAsync()));

            await using (var interrupted = await OpenAsync())
            {
                await interrupted.Stream.WriteAsync(Message(KnownId, [8]));
                AssertAck(await ReadFrameAsync(interrupted.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 18 }, Decode(await ReadFrameAsync(interrupted.Stream, TimeSpan.FromSeconds(5))).Data);
                schedule = (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json))[0];
                Assert.Equal("Delivering", schedule.GetProperty("status").GetString());
                using var cancelBusy = await fx.Client.PostAsync($"{path}/{scheduleId}/cancel", null);
                Assert.Equal(HttpStatusCode.Conflict, cancelBusy.StatusCode);
                var activeBatchId = schedule.GetProperty("lastBatchId").GetGuid();
                using var cancelActiveBatch = await fx.Client.PostAsync($"/api/downloads/{activeBatchId}/cancel?confirm=true", null);
                Assert.Equal(HttpStatusCode.BadRequest, cancelActiveBatch.StatusCode);

                await using var competing = await OpenAsync();
                await competing.Stream.WriteAsync(Message(KnownId, [8]));
                AssertAck(await ReadFrameAsync(competing.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 13 }, Decode(await ReadFrameAsync(competing.Stream, TimeSpan.FromSeconds(5))).Data);
            }
            await WaitNewBatchAsync(before, 3);
            schedule = (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json))[0];
            Assert.Equal("Ready", schedule.GetProperty("status").GetString());
            Assert.NotEqual(JsonValueKind.Null, schedule.GetProperty("lastError").ValueKind);
            var failedBatchId = schedule.GetProperty("lastBatchId").GetGuid();
            using var manualRetry = await fx.Client.PostAsync($"/api/downloads/{failedBatchId}/retry?confirm=true", null);
            Assert.Equal(HttpStatusCode.BadRequest, manualRetry.StatusCode);
            var unchanged = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/terminals/{terminalId}/table-assignment", Json);
            Assert.Equal(previousSetId, unchanged.GetProperty("tableSetId").GetGuid());

            await using (var callback = await OpenAsync())
            {
                await callback.Stream.WriteAsync(Message(KnownId, [9]));
                AssertAck(await ReadFrameAsync(callback.Stream, TimeSpan.FromSeconds(5)));
                var header = Decode(await ReadFrameAsync(callback.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 18 }, header.Data);
                await callback.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
                for (var offset = 0; offset < payload.Length; offset += NccConstants.MaxDataSize)
                {
                    var part = Decode(await ReadFrameAsync(callback.Stream, TimeSpan.FromSeconds(5)));
                    Assert.Equal(payload[offset..Math.Min(offset + NccConstants.MaxDataSize, payload.Length)], part.Data);
                    await callback.Stream.WriteAsync(Control((byte)(NccControl.Ack | part.Control)));
                }
                await callback.Stream.WriteAsync(Message(KnownId, [14, 73]));
                AssertAck(await ReadFrameAsync(callback.Stream, TimeSpan.FromSeconds(5)));
                var end = Decode(await ReadFrameAsync(callback.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 13 }, end.Data);
                await callback.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
                await WaitNewBatchAsync(before, 2);
            }

            schedule = (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json))[0];
            Assert.Equal("Delivered", schedule.GetProperty("status").GetString());
            var assignment = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/terminals/{terminalId}/table-assignment", Json);
            Assert.Equal(setId, assignment.GetProperty("tableSetId").GetGuid());
            Assert.Equal(previousSetId, assignment.GetProperty("previousTableSetId").GetGuid());

            var delivered = await BatchIdsAsync();
            using var another = await fx.Client.PostAsJsonAsync(path, new
            {
                body.targetType, body.targetId, body.terminalIds, availableAtUtc = (DateTimeOffset?)null
            }, Json);
            another.EnsureSuccessStatusCode();
            var newSchedule = (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json))[0].GetProperty("id").GetGuid();
            using var cancel = await fx.Client.PostAsync($"{path}/{newSchedule}/cancel", null);
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
            var history = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/tables/schedules?terminalId={terminalId}", Json);
            Assert.Contains(history.EnumerateArray(), row =>
                row.GetProperty("id").GetGuid() == newSchedule && row.GetProperty("status").GetString() == "Cancelled");

            await using var routine = await OpenAsync();
            await routine.Stream.WriteAsync(Message(KnownId, [8]));
            AssertAck(await ReadFrameAsync(routine.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, Decode(await ReadFrameAsync(routine.Stream, TimeSpan.FromSeconds(5))).Data);
            Assert.True(delivered.SetEquals(await BatchIdsAsync()));
        }
        finally
        {
            fx.Factory.Clock.Advance(TimeSpan.Zero);
            using var cleanup = await fx.Client.PostAsync($"{path}/{scheduleId}/cancel", null);
            await AssignLabSetAsync();
        }
    }

    [Fact]
    public async Task Firmware_install_request_does_not_send_fixture_payloads()
    {
        await AssignLabSetAsync();
        var before = await BatchIdsAsync();
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, [44, 2]));
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        var end = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(new byte[] { 13 }, end.Data);

        var batch = await WaitNewBatchAsync(before, 3);
        Assert.Contains("complete DLOG table records", batch.GetProperty("lastError").GetString());
        Assert.All(batch.GetProperty("timeline").EnumerateArray(), step =>
            Assert.Equal("not_sent", step.GetProperty("lastAckStatus").GetString()));
    }

    [Fact]
    public async Task Firmware_install_paces_fragments_and_waits_for_table_and_end_acknowledgements()
    {
        var payload = new byte[NccConstants.MaxDataSize + 7];
        payload[0] = 73;
        payload[^1] = 0x55;
        await AssignRateTableSetAsync("NCC fragmented transfer test", payload);
        try
        {
            var before = await BatchIdsAsync();
            await using var link = await OpenAsync();
            await link.Stream.WriteAsync(Message(KnownId, [44, 2]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var header = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 18 }, header.Data);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(500)));
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));

            var first = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(1, first.Control);
            Assert.Equal(payload[..NccConstants.MaxDataSize], first.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Nack | first.Control)));
            var retry = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(first.Control | NccControl.ReTrans, retry.Control);
            Assert.Equal(first.Data, retry.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | first.Control)));

            var second = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(2, second.Control);
            Assert.Equal(payload[NccConstants.MaxDataSize..], second.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | second.Control)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(500)));
            await link.Stream.WriteAsync(Message(KnownId, [14, 73]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));

            var end = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(3, end.Control);
            Assert.Equal(new byte[] { 13 }, end.Data);
            await WaitNewBatchAsync(before, 1);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
            var batch = await WaitNewBatchAsync(before, 2);
            Assert.Equal("Completed", batch.GetProperty("timeline")[0].GetProperty("hostDownloadPhaseName").GetString());
        }
        finally
        {
            await AssignLabSetAsync();
        }
    }

    [Fact]
    public async Task Missing_ack_fails_the_batch_and_retry_is_accepted()
    {
        await AssignRateTableSetAsync("NCC missing acknowledgement test");
        var before = await BatchIdsAsync();
        await using (var link = await OpenAsync())
        {
            await RequestRateTableAsync(link.Stream);
        }

        var batch = await WaitNewBatchAsync(before, 3);
        Assert.Contains("acknowledgement was missing", batch.GetProperty("lastError").GetString(), StringComparison.OrdinalIgnoreCase);
        var id = batch.GetProperty("id").GetGuid();
        var retry = await fx.Client.PostAsync($"/api/downloads/{id}/retry?confirm=true", null);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var created = JsonSerializer.Deserialize<JsonElement>(await retry.Content.ReadAsStringAsync(), Json);
        Assert.NotEqual(id, created.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Negative_ack_fails_the_batch_and_keeps_the_frame()
    {
        await AssignRateTableSetAsync("NCC negative acknowledgement test");
        var before = await BatchIdsAsync();
        byte[] nak;
        await using (var link = await OpenAsync())
        {
            await RequestRateTableAsync(link.Stream);
            nak = Message(KnownId, [14, 0x00]);
            await link.Stream.WriteAsync(nak);
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        }

        var batch = await WaitNewBatchAsync(before, 3);
        var error = batch.GetProperty("lastError").GetString() ?? "";
        Assert.Contains("negative", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Convert.ToHexString(nak), error, StringComparison.OrdinalIgnoreCase);
        var retry = await fx.Client.PostAsync($"/api/downloads/{batch.GetProperty("id").GetGuid()}/retry?confirm=true", null);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    private async Task AssignLabSetAsync()
    {
        var sets = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/sets", Json))!;
        var lab = sets.EnumerateArray().First(s => s.GetProperty("name").GetString() == "Lab install set");
        await AssignSetAsync(lab.GetProperty("id").GetGuid());
    }

    private async Task AssignRateTableSetAsync(string name, byte[]? payload = null)
        => await AssignSetAsync(await CreateRateTableSetAsync(name, payload));

    private async Task<Guid> CreateRateTableSetAsync(string name, byte[]? payload = null)
    {
        payload ??= new byte[1192];
        payload[0] = 73;
        var definitions = await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/definitions", Json);
        var definition = definitions.EnumerateArray().Single(row => row.GetProperty("tableNumber").GetInt32() == 73);
        using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name, isDefault = false }, Json);
        created.EnsureSuccessStatusCode();
        var set = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        var setId = set.GetProperty("id").GetGuid();
        using var version = await fx.Client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = setId,
            tableDefinitionId = definition.GetProperty("id").GetGuid(),
            tableRevision = 1,
            payloadBase64 = Convert.ToBase64String(payload),
            sortOrder = 0
        }, Json);
        version.EnsureSuccessStatusCode();
        using var published = await fx.Client.PostAsync($"/api/tables/sets/{setId}/publish?confirm=true", null);
        published.EnsureSuccessStatusCode();
        return setId;
    }

    private static async Task RequestRateTableAsync(NetworkStream stream)
    {
        await stream.WriteAsync(Message(KnownId, [44, 2]));
        AssertAck(await ReadFrameAsync(stream, TimeSpan.FromSeconds(5)));
        var start = Decode(await ReadFrameAsync(stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(new byte[] { 18 }, start.Data);
        await stream.WriteAsync(Control((byte)(NccControl.Ack | start.Control)));
        var firstFragment = Decode(await ReadFrameAsync(stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(73, firstFragment.Data[0]);
    }

    private async Task AssignSetAsync(Guid setId)
    {
        var terminals = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json))!;
        var term = terminals.EnumerateArray().First(t =>
            string.Equals(t.GetProperty("terminalIdHex").GetString(), "0102030405", StringComparison.OrdinalIgnoreCase));
        var resp = await fx.Client.PostAsJsonAsync(
            $"/api/terminals/{term.GetProperty("id").GetGuid()}/table-assignment",
            new { tableSetId = setId, customerId = (Guid?)null, siteId = (Guid?)null },
            Json);
        resp.EnsureSuccessStatusCode();
    }

    private async Task<HashSet<Guid>> BatchIdsAsync()
    {
        var rows = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/downloads", Json))!;
        return rows.EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToHashSet();
    }

    private async Task<JsonElement> WaitNewBatchAsync(HashSet<Guid> before, int status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var rows = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/downloads", Json))!;
            foreach (var row in rows.EnumerateArray())
            {
                var id = row.GetProperty("id").GetGuid();
                if (before.Contains(id) || row.GetProperty("status").GetInt32() != status)
                    continue;
                return (await fx.Client.GetFromJsonAsync<JsonElement>($"/api/downloads/{id}", Json))!;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No new download batch with status {status}.");
    }

    private async Task<JsonElement> WaitSessionAsync(string frameHex)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var list = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/ncc/sessions?includeArchived=true", Json))!;
            foreach (var row in list.EnumerateArray())
            {
                var sample = row.GetProperty("lastFrameSampleHex").GetString();
                if (sample != null
                    && sample.Contains(frameHex, StringComparison.OrdinalIgnoreCase)
                    && row.GetProperty("status").GetInt32() == 1)
                    return row;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Session did not close.");
    }

    private async Task<JsonElement> WaitCallAsync(string digits)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var calls = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/call-records", Json))!;
            foreach (var row in calls.EnumerateArray())
            {
                if (row.GetProperty("dialedDigits").GetString() == digits)
                    return row;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No call for {digits}.");
    }

    private async Task<List<JsonElement>> EventsAsync(string correlation)
    {
        var rows = (await fx.Client.GetFromJsonAsync<JsonElement>(
            "/api/audit/events?category=ncc&q=" + Uri.EscapeDataString(correlation), Json))!;
        return rows.EnumerateArray().Where(e =>
            (e.GetProperty("correlationId").GetString() ?? "") == correlation
            || (e.GetProperty("detailJson").GetString() ?? "").Contains(correlation, StringComparison.Ordinal)).ToList();
    }

    private async Task<Link> OpenAsync()
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
        return new Link(tcp);
    }

    [Fact]
    public async Task Alarm_frame_is_listed_for_the_operator()
    {
        var before = await BatchIdsAsync();
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, [7, 0x01, 0x02]));
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        await Task.Delay(400);
        var after = await BatchIdsAsync();
        Assert.Equal(before.Count, after.Count);

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var list = await fx.Client.GetFromJsonAsync<JsonElement>("/api/field-events", Json);
            var items = list!.GetProperty("items");
            if (items.EnumerateArray().Any(row => row.GetProperty("title").GetString() == "Alarm"))
                return;
            await Task.Delay(100);
        }

        Assert.Fail("Alarm was not listed.");
    }

    [Fact]
    public async Task Complete_alarm_dialogue_receives_alarm_acknowledgements_and_end_data()
    {
        var bundle = new byte[2 * 8 + 11 + 28 + 57 + 1];
        bundle[0] = 7;
        bundle[7] = 22;
        bundle[8] = 7;
        bundle[15] = 91;
        bundle[16] = 10;
        bundle[25] = 0x01;
        bundle[27] = 60;
        bundle[55] = 38;
        bundle[^1] = 13;
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, bundle));
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        var reply = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(KnownId, reply.TerminalId);
        Assert.Equal(new byte[] { 16, 22, 16, 91, 13 }, reply.Data);

        var events = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/field-events", Json))!;
        var rows = events.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(rows, row => row.GetProperty("summary").GetString() == "Memory lost (alarm 22)");
        Assert.Contains(rows, row => row.GetProperty("summary").GetString() == "Unlisted alarm 91");
        Assert.Contains(rows, row => row.GetProperty("summary").GetString() == "Access cover open");
        Assert.Contains(rows, row => row.GetProperty("category").GetString() == "cash");
    }

    [Fact]
    public async Task Complete_maintenance_dialogue_echoes_opcodes_without_starting_a_download()
    {
        await AssignLabSetAsync();
        var batches = await BatchIdsAsync();
        var bundle = new byte[2 * 6 + 11 + 28 + 1];
        byte[] requests = [6, 0xE4, 0x03, 1, 2, 3, 6, 0xE7, 0x03, 4, 5, 6];
        requests.CopyTo(bundle, 0);
        bundle[12] = 10;
        bundle[23] = 60;
        bundle[^1] = 13;

        var sent = Message(KnownId, bundle);
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(sent);
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var reply = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(KnownId, reply.TerminalId);
            Assert.Equal(new byte[] { 15, 0xE4, 0x03, 15, 0xE7, 0x03, 13 }, reply.Data);

            var sessions = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/ncc/sessions", Json))!;
            var session = sessions.EnumerateArray().First(row => row.GetProperty("socketBound").GetBoolean());
            Assert.Equal("", session.GetProperty("lastFrameSampleHex").GetString());
            var terminalId = session.GetProperty("terminalId").GetGuid();
            var events = (await fx.Client.GetFromJsonAsync<JsonElement>($"/api/field-events?terminalId={terminalId}", Json))!;
            var maintenance = events.GetProperty("items").EnumerateArray()
                .Where(row => row.GetProperty("category").GetString() == "maintenance").ToList();
            Assert.Contains(maintenance, row => row.GetProperty("summary").GetString() == "Technician entered maintenance mode (opcode 996)");
            Assert.Contains(maintenance, row => row.GetProperty("summary").GetString() == "Terminal out of service (opcode 999)");
            Assert.All(maintenance, row => Assert.Equal(3, row.GetProperty("byteCount").GetInt32()));
        }

        Assert.True(batches.SetEquals(await BatchIdsAsync()));
    }

    private static byte[] Message(byte[] id, byte[] data) =>
        NccFrameCodec.Encode(new NccWirePacket { Control = 0, TerminalId = id, Data = data });

    private static byte[] Control(byte control) =>
        NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = KnownId });

    private static byte[] HeaderOnlyRetransmission(byte[] id, byte packetId)
    {
        byte[] frame = [NccConstants.FrameStart, (byte)(NccControl.ReTrans | packetId), 10, ..id, 0, 0, NccConstants.FrameEnd];
        var crc = NccCrc16.Compute(frame.AsSpan(0, frame.Length - 3));
        frame[^3] = (byte)crc;
        frame[^2] = (byte)(crc >> 8);
        return frame;
    }

    private static void AssertAck(byte[] frame)
    {
        var parsed = Decode(frame);
        Assert.True(NccControl.IsControlPacket(parsed.Control));
        Assert.NotEqual(0, parsed.Control & NccControl.Ack);
        Assert.Equal(NccConstants.ControlPacketCount, parsed.Count);
    }

    private static NccWirePacket Decode(byte[] frame)
    {
        var parsed = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(parsed.Success, string.Join("; ", parsed.Diagnostics));
        return parsed.Packet!;
    }

    private static async Task<NccWirePacket> ReadUntilAsync(NetworkStream stream, Func<NccWirePacket, bool> match)
    {
        for (var i = 0; i < 12; i++)
        {
            var parsed = Decode(await ReadFrameAsync(stream, TimeSpan.FromSeconds(5)));
            if (match(parsed))
                return parsed;
        }

        throw new TimeoutException("Expected frame was not written.");
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var head = new byte[3];
        await ReadExactAsync(stream, head, cts.Token);
        var len = NccFrameLayout.GetExpectedWireLength(head[2]);
        var frame = new byte[len];
        head.AsSpan().CopyTo(frame);
        if (len > 3)
            await ReadExactAsync(stream, frame.AsMemory(3), cts.Token);
        return frame;
    }

    private static async Task ReadExactAsync(NetworkStream stream, Memory<byte> dest, CancellationToken ct)
    {
        var off = 0;
        while (off < dest.Length)
        {
            var n = await stream.ReadAsync(dest[off..], ct);
            if (n == 0)
                throw new EndOfStreamException();
            off += n;
        }
    }

    private sealed class Link(TcpClient tcp) : IAsyncDisposable
    {
        public NetworkStream Stream { get; } = tcp.GetStream();

        public ValueTask DisposeAsync()
        {
            tcp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
