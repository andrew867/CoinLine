using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
        Client?.Dispose();
        if (Factory is not null)
            await Factory.DisposeAsync();
    }
}

public sealed class NccFactory : WebApplicationFactory<Program>
{
    public int Port { get; }

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
    }
}

public class NccOperationalTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] KnownId = Convert.FromHexString("0102030405");
    private static readonly byte[] UnknownId = Convert.FromHexString("AABBCCDDEE");

    [Fact]
    public async Task Known_id_is_stored_and_acked()
    {
        var sent = Message(KnownId, [0x01, 0x11, 0x22]);
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
        var sent = Message(UnknownId, [0x01, 0xAB]);
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
        Assert.Equal("AABBCCDDEE", frame.GetProperty("terminalIdHex").GetString());
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
        var first = Message(KnownId, [0x01, 0x31]);
        var second = Message(KnownId, [0x01, 0x32]);
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
        await AssignLabSetAsync();
        var batches = await BatchIdsAsync();
        var allowed = DlogRateWire.EncodeRequest("5551212", international: true);
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(KnownId, allowed));
            var response = await ReadUntilAsync(link.Stream, p => p.Data.Length > 0 && p.Data[0] == DlogRateWire.MessageTypeResponse);
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
            var response = await ReadUntilAsync(link.Stream, p => p.Data.Length > 0 && p.Data[0] == DlogRateWire.MessageTypeResponse);
            Assert.Equal(DlogRateWire.RateBlocked, response.Data[1]);
            Assert.Equal(0, response.Data[4]);
        }

        var blocked = await WaitCallAsync("9005550100");
        Assert.Equal(2, blocked.GetProperty("disposition").GetInt32());

        var empty = DlogRateWire.EncodeRequest("", false);
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(KnownId, empty));
            var response = await ReadUntilAsync(link.Stream, p => p.Data.Length > 0 && p.Data[0] == DlogRateWire.MessageTypeResponse);
            Assert.Equal(DlogRateWire.ResponseLength, response.Data.Length);
            Assert.Equal(DlogRateWire.RateBlocked, response.Data[1]);
        }

        var blank = await WaitCallAsync("");
        Assert.Equal("", blank.GetProperty("dialedDigits").GetString());
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
    public async Task Install_set_completes_on_type_14()
    {
        await AssignLabSetAsync();
        var before = await BatchIdsAsync();
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, [0x01, 0x71]));
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        var types = new List<byte>();
        for (var i = 0; i < 5; i++)
        {
            var parsed = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.False(NccControl.IsControlPacket(parsed.Control));
            Assert.True(parsed.Data.Length >= 2);
            types.Add(parsed.Data[0]);
            Assert.Equal(0x01, parsed.Data[1]);
        }

        Assert.Equal(new byte[] { 21, 26, 31, 33, 73 }, types);
        foreach (var type in types)
        {
            await link.Stream.WriteAsync(Message(KnownId, [14, type]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        }

        var batch = await WaitNewBatchAsync(before, 2);
        Assert.Equal(2, batch.GetProperty("status").GetInt32());
        Assert.All(batch.GetProperty("timeline").EnumerateArray(), step =>
            Assert.Equal("Completed", step.GetProperty("hostDownloadPhaseName").GetString()));
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
        var definitions = await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/definitions", Json);
        var definition = definitions.EnumerateArray().Single(row => row.GetProperty("tableNumber").GetInt32() == 73);
        using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets",
            new { name = "NCC transfer test", isDefault = false }, Json);
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

        await AssignSetAsync(setId);
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
        await AssignLabSetAsync();
        var before = await BatchIdsAsync();
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(KnownId, [0x01, 0x72]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var first = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(21, first.Data[0]);
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
        await AssignLabSetAsync();
        var before = await BatchIdsAsync();
        byte[] nak;
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(KnownId, [0x01, 0x73]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            for (var i = 0; i < 5; i++)
                await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5));
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
        bundle[7] = 90;
        bundle[8] = 7;
        bundle[15] = 91;
        bundle[16] = 10;
        bundle[27] = 60;
        bundle[55] = 38;
        bundle[^1] = 13;
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, bundle));
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        var reply = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(KnownId, reply.TerminalId);
        Assert.Equal(new byte[] { 16, 90, 16, 91, 13 }, reply.Data);
    }

    [Fact]
    public async Task Complete_maintenance_dialogue_echoes_opcodes_without_starting_a_download()
    {
        await AssignLabSetAsync();
        var batches = await BatchIdsAsync();
        var bundle = new byte[2 * 6 + 11 + 28 + 1];
        byte[] requests = [6, 0x12, 0x34, 1, 2, 3, 6, 0x56, 0x78, 4, 5, 6];
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
            Assert.Equal(new byte[] { 15, 0x12, 0x34, 15, 0x56, 0x78, 13 }, reply.Data);
        }

        await WaitSessionAsync(Convert.ToHexString(sent));
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
