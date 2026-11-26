using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HostPlatform.Api.Hosting;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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
        Client = TestOperatorAuthentication.CreateAuthenticatedClient(Factory, "ui@local", "Admin");
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
        builder.UseEnvironment("Testing").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey);
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
    public async Task Deactivated_phone_is_matched_and_closed_without_ack_then_reactivation_restores_contact()
    {
        var wireId = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        Guid terminalId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new HostPlatform.Domain.Terminal { SiteId = site.Id, DisplayName = "Lifecycle fixture", TerminalIdHex = wireId };
            db.Terminals.Add(phone);
            await db.SaveChangesAsync();
            terminalId = phone.Id;
        }
        var path = $"/api/terminals/{terminalId}/lifecycle";
        var initial = await fx.Client.GetFromJsonAsync<JsonElement>(path);
        var revision = initial.GetProperty("revision").GetInt32();
        var id = Convert.FromHexString(wireId);
        var first = Message(id, [84, 0x21]);
        await using (var connected = await OpenAsync())
        {
            await connected.Stream.WriteAsync(first);
            AssertAck(await ReadFrameAsync(connected.Stream, TimeSpan.FromSeconds(5)));
            using var refused = await fx.Client.PutAsJsonAsync(path, new { active = false, revision });
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        }
        await WaitSessionAsync(Convert.ToHexString(first));
        using (var deactivate = await fx.Client.PutAsJsonAsync(path, new { active = false, revision }))
        {
            deactivate.EnsureSuccessStatusCode();
            revision = (await deactivate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt32();
        }

        var blocked = Message(id, [84, 0x22]);
        await using (var inactive = await OpenAsync())
        {
            await inactive.Stream.WriteAsync(blocked);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.Equal(0, await inactive.Stream.ReadAsync(new byte[64], timeout.Token));
        }
        var recorded = await WaitSessionAsync(Convert.ToHexString(blocked));
        Assert.Equal(terminalId, recorded.GetProperty("terminalId").GetGuid());
        Assert.Equal(1, recorded.GetProperty("status").GetInt32());

        using (var reactivate = await fx.Client.PutAsJsonAsync(path, new { active = true, revision }))
            reactivate.EnsureSuccessStatusCode();
        var returned = Message(id, [84, 0x23]);
        await using (var active = await OpenAsync())
        {
            await active.Stream.WriteAsync(returned);
            AssertAck(await ReadFrameAsync(active.Stream, TimeSpan.FromSeconds(5)));
        }
        await WaitSessionAsync(Convert.ToHexString(returned));
    }

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
    public async Task Session_capture_keeps_direction_and_timing_and_omits_identifiers_and_payloads()
    {
        const string inboundSecret = "fixture-terminal-payload";
        const string outboundSecret = "fixture-craft-payload";
        var inbound = Message(KnownId, [84, .. Encoding.ASCII.GetBytes(inboundSecret)]);
        var outbound = Message(KnownId, [85, .. Encoding.ASCII.GetBytes(outboundSecret)]);
        var clear = Control(NccControl.Clr);
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(inbound);
        var ack = await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5));
        AssertAck(ack);

        var row = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/ncc/sessions", Json)).EnumerateArray()
            .Single(session => session.GetProperty("socketBound").GetBoolean() &&
                session.GetProperty("lastFrameSampleHex").GetString() == Convert.ToHexString(inbound));
        var sessionId = row.GetProperty("id").GetGuid();
        var terminalId = row.GetProperty("terminalId").GetGuid();
        var links = fx.Factory.Services.GetRequiredService<INccSessionOutbound>();
        await links.WriteFramedAsync(terminalId, outbound, CancellationToken.None);
        Assert.Equal(outbound, await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        await link.Stream.WriteAsync(clear);
        await WaitSessionAsync(Convert.ToHexString(clear), sessionId);
        var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/ncc/sessions/{sessionId}", Json);
        Assert.Equal(2, detail.GetProperty("frames").GetArrayLength());

        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var storedTimes = await db.NccSessionFrames.AsNoTracking()
            .Where(frame => frame.NccSessionId == sessionId)
            .OrderBy(frame => frame.Sequence)
            .Select(frame => frame.CreatedAtUtc)
            .ToListAsync();

        using var response = await fx.Client.GetAsync($"/api/ncc/sessions/{sessionId}/capture");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-pcapng", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("coinline-ncc-redacted.pcapng", response.Content.Headers.ContentDisposition!.ToString());
        Assert.True(response.Headers.CacheControl?.NoStore);
        var capture = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(-1, capture.AsSpan().IndexOf(KnownId));
        Assert.Equal(-1, capture.AsSpan().IndexOf(Encoding.ASCII.GetBytes(inboundSecret)));
        Assert.Equal(-1, capture.AsSpan().IndexOf(Encoding.ASCII.GetBytes(outboundSecret)));
        Assert.Equal(-1, capture.AsSpan().IndexOf(Encoding.ASCII.GetBytes(sessionId.ToString())));
        var packets = new List<(uint Direction, byte Control, uint Length)>();
        var captureTimes = new List<DateTime>();
        for (var offset = 0; offset < capture.Length;)
        {
            var type = ReadUInt(offset);
            var blockSize = checked((int)ReadUInt(offset + 4));
            Assert.Equal((uint)blockSize, ReadUInt(offset + blockSize - 4));
            if (type == 6)
            {
                var timestamp = ((ulong)ReadUInt(offset + 12) << 32) | ReadUInt(offset + 16);
                captureTimes.Add(DateTime.UnixEpoch.AddTicks(checked((long)timestamp * TimeSpan.TicksPerMicrosecond)));
                Assert.Equal(1u, ReadUInt(offset + 20));
                Assert.Equal(0x00040002u, ReadUInt(offset + 32));
                packets.Add((ReadUInt(offset + 36), capture[offset + 28], ReadUInt(offset + 24)));
            }
            offset += blockSize;
        }

        Assert.Equal([(1u, inbound[1], (uint)inbound.Length), (2u, ack[1], (uint)ack.Length),
            (2u, outbound[1], (uint)outbound.Length), (1u, clear[1], (uint)clear.Length)], packets);
        Assert.Equal(storedTimes.Select(time => time.AddTicks(-(time.Ticks % TimeSpan.TicksPerMicrosecond))), captureTimes);

        uint ReadUInt(int at) => BinaryPrimitives.ReadUInt32LittleEndian(capture.AsSpan(at, 4));
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

    [Theory]
    [InlineData(1, 29, 3, 2, "approve", "approved")]
    [InlineData(2, 39, 65, 22, "approve", "approved")]
    [InlineData(2, 39, 65, 22, "decline", "declined")]
    [InlineData(2, 39, 65, 22, "invalid", "provider_invalid_decision")]
    [InlineData(2, 39, 65, 22, "exception", "provider_failed")]
    public async Task Trusted_card_provider_is_fail_closed_retries_are_idempotent_and_audit_is_redacted(
        byte requestType, int requestLength, byte replyType, int replyLength, string mode, string expectedOutcome)
    {
        var request = new byte[requestLength];
        request[0] = requestType;
        // Opaque synthetic test pattern; no account number, track data or issuer credential.
        request[^3] = 0x5A;
        request[^5] = 0xA5;
        var provider = new OpaqueTestCardProvider(request, mode);
        await using var factory = new NccFactory();
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<INccCardAuthorizationProvider>(provider)));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(host, "fixture", "Admin");
        var state = await client.GetFromJsonAsync<JsonElement>("/api/cards/simulation-state");
        Assert.True(state.GetProperty("runtimeAuthorizationProviderRegistered").GetBoolean());
        Assert.Equal(0, provider.Calls);
        Guid terminalId;
        int beforeBatches;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            terminalId = (await db.Terminals.SingleAsync(row => row.TerminalIdHex == Convert.ToHexString(KnownId))).Id;
            beforeBatches = await db.DownloadBatches.CountAsync();
        }
        provider.TerminalId = terminalId;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, factory.Port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Message(KnownId, request));
        AssertAck(await ReadFrameAsync(stream, TimeSpan.FromSeconds(5)));
        var response = Decode(await ReadFrameAsync(stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(replyType, response.Data[0]);
        Assert.Equal(replyLength, response.Data.Length);
        Assert.Equal(mode == "approve" ? (byte)0 : (byte)1, response.Data[1]);
        if (requestType == 2 && mode == "approve")
            Assert.Equal("TESTREF1"u8.ToArray(), response.Data[2..10]);
        Assert.Equal(1, provider.Calls);

        // Both full and header-only retransmission preserve the original reply and do not call the provider twice.
        await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket
            { Control = NccControl.ReTrans, TerminalId = KnownId, Data = request }));
        AssertAck(await ReadFrameAsync(stream, TimeSpan.FromSeconds(5)));
        var repeated = Decode(await ReadFrameAsync(stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(response.Data, repeated.Data);
        Assert.Equal(response.Control | NccControl.ReTrans, repeated.Control);
        await stream.WriteAsync(HeaderOnlyRetransmission(KnownId, 0));
        AssertAck(await ReadFrameAsync(stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(response.Data, Decode(await ReadFrameAsync(stream, TimeSpan.FromSeconds(5))).Data);
        Assert.Equal(1, provider.Calls);
        await stream.WriteAsync(Control((byte)(NccControl.Ack | response.Control)));
        await stream.WriteAsync(Control(NccControl.Clr));

        await using var check = host.Services.CreateAsyncScope();
        var stored = check.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var audit = await stored.AuditEvents.SingleAsync(row => row.Action == "card_authorization_decision");
        var metadata = JsonSerializer.Deserialize<JsonElement>(audit.DetailJson, Json);
        Assert.Equal(expectedOutcome, metadata.GetProperty("outcome").GetString());
        Assert.Equal(3, metadata.EnumerateObject().Count());
        Assert.Equal("trusted_provider", metadata.GetProperty("provider").GetString());
        Assert.Equal(requestType, metadata.GetProperty("messageType").GetByte());
        Assert.Equal(beforeBatches, await stored.DownloadBatches.CountAsync());
        var sessionId = Guid.Parse(audit.TerminalSessionId!);
        var frames = await stored.NccSessionFrames.Where(row => row.NccSessionId == sessionId).ToListAsync();
        var protectedFrames = frames.Where(frame => !frame.SentByHost && frame.WireLength == requestLength + 11).ToArray();
        Assert.Equal(2, protectedFrames.Length);
        Assert.All(protectedFrames, frame => Assert.Empty(frame.RawBytes));
        Assert.All(frames.Where(frame => frame.SentByHost), frame => Assert.Empty(frame.RawBytes));
    }

    private sealed class OpaqueTestCardProvider(byte[] expectedRequest, string mode) : INccCardAuthorizationProvider
    {
        public Guid TerminalId { get; set; }
        public int Calls { get; private set; }

        public ValueTask<NccCardAuthorizationDecision> AuthorizeAsync(Guid terminalId, ReadOnlyMemory<byte> protectedRequest,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (terminalId != TerminalId || !protectedRequest.Span.SequenceEqual(expectedRequest))
                return ValueTask.FromResult(new NccCardAuthorizationDecision());
            if (mode == "exception")
                throw new InvalidOperationException("Synthetic provider error");
            return ValueTask.FromResult(new NccCardAuthorizationDecision
            {
                Approved = mode is "approve" or "invalid",
                AuthorizationReference = mode == "invalid" ? new byte[1]
                    : mode == "approve" && expectedRequest[0] == 2 ? "TESTREF1"u8.ToArray() : []
            });
        }
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
    public async Task Cashbox_quarter_call_round_trip_separates_quote_deposit_and_collection_across_reconnects()
    {
        const string wireId = "0000000961";
        var wire = Convert.FromHexString(wireId);
        var seed = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json)).EnumerateArray()
            .Single(row => row.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId));
        using var phone = await fx.Client.PostAsJsonAsync("/api/terminals", new
        {
            siteId = seed.GetProperty("siteId").GetGuid(), displayName = "Cashbox quarter fixture", terminalIdHex = wireId, status = 0
        }, Json);
        phone.EnsureSuccessStatusCode();
        var terminalId = (await phone.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var cashboxPath = $"/api/terminals/{terminalId}/cashbox";
        var setId = await CreateRateTableSetAsync("Cashbox combined request fixture");
        using var queue = await fx.Client.PostAsJsonAsync($"/api/terminals/{terminalId}/downloads", new { tableSetId = setId }, Json);
        queue.EnsureSuccessStatusCode();
        var queued = (await queue.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var tableBatches = await BatchIdsAsync();

        var rateRequest = DlogRateWire.EncodeRequest("5551212", international: true);
        var quote = await ExchangeAsync(rateRequest);
        Assert.Equal(DlogRateWire.MessageTypeResponse, quote[0]);
        var quotedCents = BinaryPrimitives.ReadUInt16LittleEndian(quote.AsSpan(4));
        Assert.Equal(2, quotedCents);

        var cdr = TerminalCallDetail(61, 0x0246, 0x33);
        rateRequest.AsSpan(3, 10).CopyTo(cdr.AsSpan(2));
        BinaryPrimitives.WriteInt32LittleEndian(cdr.AsSpan(13), 25);
        BinaryPrimitives.WriteInt32LittleEndian(cdr.AsSpan(17), quotedCents);
        var status = new byte[57];
        status[0] = 38;
        byte[] terminalTime = [126, 11, 24, 12, 0, 0];
        terminalTime.CopyTo(status, 1);
        terminalTime.CopyTo(cdr, 25);
        Convert.FromHexString("00001001").CopyTo(status, 8);
        status[12] = 17;
        BinaryPrimitives.WriteInt32LittleEndian(status.AsSpan(13), 75);
        BinaryPrimitives.WriteUInt16LittleEndian(status.AsSpan(23), 3);
        status[^1] = 0x32;
        byte[] callAndStatus = [.. cdr, .. status, 13];
        Assert.Equal(new byte[] { 5, 0x46, 0x02 }, await ExchangeAsync(callAndStatus));
        Assert.Equal(new byte[] { 5, 0x46, 0x02 }, await ExchangeAsync(callAndStatus));

        byte[] requestStatus = [44, 0x80];
        byte[] expectedStatus = [18, .. status, 13];
        await using (var restore = await OpenAsync())
        {
            await restore.Stream.WriteAsync(Message(wire, requestStatus));
            AssertAck(await ReadFrameAsync(restore.Stream, TimeSpan.FromSeconds(5)));
            var reply = Decode(await ReadFrameAsync(restore.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(expectedStatus, reply.Data);
            await restore.Stream.WriteAsync(HeaderOnlyRetransmission(wire, 0));
            AssertAck(await ReadFrameAsync(restore.Stream, TimeSpan.FromSeconds(5)));
            var replay = Decode(await ReadFrameAsync(restore.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(reply.Control | NccControl.ReTrans, replay.Control);
            Assert.Equal(reply.Data, replay.Data);
            await restore.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | reply.Control)));
        }

        var collection = new byte[71];
        collection[0] = 51;
        Convert.FromHexString("00001002").CopyTo(collection, 5);
        BinaryPrimitives.WriteUInt16LittleEndian(collection.AsSpan(9), 0x0247);
        BinaryPrimitives.WriteInt32LittleEndian(collection.AsSpan(11), 75);
        status.AsSpan(1).CopyTo(collection.AsSpan(15));
        collection[19] = 2;
        Assert.Equal(new byte[] { 13 }, await ExchangeAsync([.. collection, 13]));
        collection[^1] ^= 1;
        Assert.Equal(new byte[] { 13 }, await ExchangeAsync(collection));
        Assert.Equal(new byte[] { 5, 0x46, 0x02 }, await ExchangeAsync(callAndStatus));
        Assert.Equal(new byte[] { 13 }, await ExchangeAsync(requestStatus));

        var ledger = await fx.Client.GetFromJsonAsync<JsonElement>(cashboxPath, Json);
        var observed = Assert.Single(ledger.GetProperty("statuses").EnumerateArray());
        Assert.False(observed.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(75, observed.GetProperty("status").GetProperty("balanceMinorUnits").GetInt32());
        var counted = Assert.Single(ledger.GetProperty("collections").EnumerateArray());
        Assert.Equal("00001001", counted.GetProperty("boxNumber").GetString());
        Assert.Equal("00001002", counted.GetProperty("newBoxNumber").GetString());
        Assert.Equal(75, counted.GetProperty("totalMinorUnits").GetInt32());
        Assert.Equal(3, counted.GetProperty("coinCounts")[3].GetInt32());
        Assert.False(counted.TryGetProperty("collectorId", out _));
        var uploads = await fx.Client.GetFromJsonAsync<JsonElement>("/api/uploads", Json);
        var cdrRow = Assert.Single(uploads.EnumerateArray(), row => row.GetProperty("terminalId") is { ValueKind: JsonValueKind.String } id &&
            id.GetGuid() == terminalId && row.GetProperty("isTerminalCallDetail").GetBoolean());
        var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/uploads/{cdrRow.GetProperty("id").GetGuid()}", Json);
        var terminalCall = detail.GetProperty("records")[0].GetProperty("terminalCall");
        Assert.Equal(25, terminalCall.GetProperty("amountCollected").GetInt32());
        Assert.Equal((int)quotedCents, terminalCall.GetProperty("amountCharged").GetInt32());
        Assert.True(tableBatches.SetEquals(await BatchIdsAsync()));
        var stillPending = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/downloads/{queued}", Json);
        Assert.Equal(0, stillPending.GetProperty("status").GetInt32());

        // A combined collection can revise an earlier report if the phone changes the box again before clearing it.
        var combinedCollection = collection.ToArray();
        combinedCollection[1] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(combinedCollection.AsSpan(9), 0x0248);
        BinaryPrimitives.WriteInt32LittleEndian(combinedCollection.AsSpan(11), 100);
        Assert.Equal(new byte[] { 13 }, await ExchangeAsync(combinedCollection));
        var revisedLedger = await fx.Client.GetFromJsonAsync<JsonElement>(cashboxPath, Json);
        var revised = Assert.Single(revisedLedger.GetProperty("collections").EnumerateArray());
        Assert.True(revised.GetProperty("isSummed").GetBoolean());
        Assert.Equal(100, revised.GetProperty("totalMinorUnits").GetInt32());
        Assert.Equal(75, revised.GetProperty("removedBoxBalanceMinorUnits").GetInt32());

        var replacement = new byte[57];
        replacement[0] = 38;
        terminalTime.CopyTo(replacement, 1);
        replacement[5] = 2;
        replacement[6] = 1;
        Convert.FromHexString("00001002").CopyTo(replacement, 8);
        await using (var statusUpload = await OpenAsync())
        {
            await statusUpload.Stream.WriteAsync(Message(wire, [.. replacement, 13]));
            AssertAck(await ReadFrameAsync(statusUpload.Stream, TimeSpan.FromSeconds(5)));
        }
        await using var combined = await OpenAsync();
        await combined.Stream.WriteAsync(Message(wire, [44, 0x81]));
        AssertAck(await ReadFrameAsync(combined.Stream, TimeSpan.FromSeconds(5)));
        var header = Decode(await ReadFrameAsync(combined.Stream, TimeSpan.FromSeconds(5)));
        byte[] expectedHeader = [18, .. replacement];
        Assert.Equal(expectedHeader, header.Data);
        Assert.Equal(1, (await fx.Client.GetFromJsonAsync<JsonElement>($"/api/downloads/{queued}", Json)).GetProperty("status").GetInt32());
        await combined.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
        Assert.Equal(73, Decode(await ReadFrameAsync(combined.Stream, TimeSpan.FromSeconds(5))).Data[0]);
        Assert.True(tableBatches.SetEquals(await BatchIdsAsync()));

        async Task<byte[]> ExchangeAsync(byte[] request)
        {
            await using var call = await OpenAsync();
            await call.Stream.WriteAsync(Message(wire, request));
            AssertAck(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
            var response = Decode(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
            await call.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | response.Control)));
            return response.Data;
        }
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
        extended[14] = 253;
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
        var combinedCarrier = Assert.Single(reports[71].GetProperty("carriers").EnumerateArray());
        Assert.Equal(253, combinedCarrier.GetProperty("reference").GetInt32());
        Assert.Equal(11, StatisticsCounter(combinedCarrier, "Local", "Mondex"));
        Assert.Equal(3, StatisticsCounter(reports[69], "Stored-value cards", "Local"));
        Assert.Equal(5, StatisticsCounter(reports[69], "Other messaging counters", "No-answer counter"));
        Assert.True(tablesBefore.SetEquals(await BatchIdsAsync()));
    }

    [Fact]
    public async Task Performance_report_references_the_retained_mt32_ack_before_receipt_not_the_current_assignment()
    {
        var wireId = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var id = Convert.FromHexString(wireId);
        Guid terminalId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var phone = new HostPlatform.Domain.Terminal
            {
                SiteId = (await db.Sites.FirstAsync()).Id, TerminalIdHex = wireId, DisplayName = "Performance reference fixture"
            };
            db.Terminals.Add(phone);
            await db.SaveChangesAsync();
            terminalId = phone.Id;
        }

        var definitions = await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/definitions", Json);
        var definition = definitions.EnumerateArray().FirstOrDefault(row => row.GetProperty("tableNumber").GetInt32() == 32);
        Guid definitionId;
        if (definition.ValueKind == JsonValueKind.Undefined)
        {
            using var created = await fx.Client.PostAsJsonAsync("/api/tables/definitions", new { name = "Performance reference fixture", tableNumber = 32 });
            created.EnsureSuccessStatusCode();
            definitionId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        else
            definitionId = definition.GetProperty("id").GetGuid();

        var first = Timing(100);
        var second = Timing(500);
        var third = Timing(900);
        var firstSet = await Set(first, 1);
        var secondSet = await Set(second, 2);
        var thirdSet = await Set(third, 3);
        var firstBatch = await Queue(firstSet);
        await Transfer(firstBatch, first, true);

        using (var assigned = await fx.Client.PostAsJsonAsync($"/api/terminals/{terminalId}/table-assignment", new { tableSetId = secondSet }))
            assigned.EnsureSuccessStatusCode();
        var secondBatch = await Queue(secondSet);
        var original = await Upload(8);
        AssertReference(original, firstBatch, 1, 100, false);

        await Transfer(secondBatch, second, true);
        var stillOriginal = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/uploads/{original.GetProperty("id").GetGuid()}", Json);
        AssertReference(stillOriginal, firstBatch, 1, 100, false);
        var newer = await Upload(9);
        AssertReference(newer, secondBatch, 2, 500, false);

        var thirdBatch = await Queue(thirdSet);
        await Transfer(thirdBatch, third, false);
        var afterFailure = await Upload(10);
        AssertReference(afterFailure, secondBatch, 2, 500, true);

        static byte[] Timing(short start)
        {
            var payload = new byte[33];
            payload[0] = 32;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), start);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), 20);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(9), -99); // Reserved pair is not in MT37.
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(13), 333);
            return payload;
        }

        async Task<Guid> Set(byte[] payload, int revision)
        {
            using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = $"Performance reference {revision}" });
            created.EnsureSuccessStatusCode();
            var set = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var version = await fx.Client.PostAsJsonAsync("/api/tables/versions", new
            {
                tableSetId = set, tableDefinitionId = definitionId, tableRevision = revision,
                payloadBase64 = Convert.ToBase64String(payload), sortOrder = 0
            });
            version.EnsureSuccessStatusCode();
            using var published = await fx.Client.PostAsync($"/api/tables/sets/{set}/publish?confirm=true", null);
            published.EnsureSuccessStatusCode();
            return set;
        }

        async Task<Guid> Queue(Guid set)
        {
            using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{terminalId}/downloads", new
            {
                tableSetId = set, scope = "Partial", partialTableDefinitionIds = new[] { definitionId }
            });
            queued.EnsureSuccessStatusCode();
            return (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        async Task Transfer(Guid batch, byte[] payload, bool acknowledge)
        {
            await using (var call = await OpenAsync())
            {
                await call.Stream.WriteAsync(Message(id, [44, 1]));
                AssertAck(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
                var begin = Decode(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 18 }, begin.Data);
                await call.Stream.WriteAsync(Control((byte)(NccControl.Ack | begin.Control)));
                var record = Decode(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(payload, record.Data);
                await call.Stream.WriteAsync(Control((byte)(NccControl.Ack | record.Control)));
                if (acknowledge)
                {
                    await call.Stream.WriteAsync(Message(id, [14, 32]));
                    AssertAck(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
                    var end = Decode(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
                    Assert.Equal(new byte[] { 13 }, end.Data);
                    await call.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
                }
            }
            var completed = await WaitBatchAsync(batch, acknowledge ? 2 : 3);
            var step = Assert.Single(completed.GetProperty("timeline").EnumerateArray());
            Assert.Equal(acknowledge, step.GetProperty("succeeded").GetBoolean());
            if (acknowledge) Assert.Equal("ack", step.GetProperty("lastAckStatus").GetString());
        }

        async Task<JsonElement> Upload(ushort count)
        {
            var report = StatisticsReport(37, 99);
            BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(21), count);
            await using (var link = await OpenAsync())
            {
                await link.Stream.WriteAsync(Message(id, report));
                AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            }
            var uploads = await fx.Client.GetFromJsonAsync<JsonElement>("/api/uploads", Json);
            var entry = uploads.EnumerateArray().First(row => row.GetProperty("terminalId").ValueKind != JsonValueKind.Null &&
                row.GetProperty("terminalId").GetGuid() == terminalId && row.GetProperty("isTerminalStatistics").GetBoolean());
            var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/uploads/{entry.GetProperty("id").GetGuid()}", Json);
            var decoded = Assert.Single(detail.GetProperty("records").EnumerateArray()).GetProperty("terminalStatistics");
            Assert.Equal(count, StatisticsCounter(decoded, "Line seizure to end of dialing", "Bucket 1"));
            return detail;
        }

        static void AssertReference(JsonElement detail, Guid batch, int revision, short start, bool uncertain)
        {
            var reference = detail.GetProperty("performanceReference");
            Assert.Equal("acknowledged", reference.GetProperty("status").GetString());
            Assert.Equal(batch, reference.GetProperty("downloadBatchId").GetGuid());
            Assert.Equal(revision, reference.GetProperty("tableRevision").GetInt32());
            Assert.True(reference.GetProperty("ackRecordedAtUtc").GetDateTime() <= detail.GetProperty("createdAtUtc").GetDateTime());
            Assert.Equal(uncertain, reference.GetProperty("laterUncertainAttempt").GetBoolean());
            var timing = reference.GetProperty("timing").EnumerateArray().ToArray();
            Assert.Equal(new[] { 0, 1, 3, 4 }, timing.Select(pair => pair.GetProperty("measurementIndex").GetInt32()));
            Assert.Equal(start, timing[0].GetProperty("startTicks").GetInt32());
            Assert.Equal(20, timing[0].GetProperty("intervalTicks").GetInt32());
            Assert.Equal(333, timing[2].GetProperty("startTicks").GetInt32());
        }
    }

    [Fact]
    public async Task Truncated_statistics_close_the_link_before_acknowledgement()
    {
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(KnownId, StatisticsReport(37, 99)[..^1]));
        await Assert.ThrowsAsync<EndOfStreamException>(
            () => ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Graphical_usage_report_chunks_are_saved_independently_and_retry_does_not_duplicate()
    {
        var tableBatches = await BatchIdsAsync();
        var before = await fx.Client.GetFromJsonAsync<JsonElement>("/api/uploads", Json);
        var existing = before.EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToHashSet();
        var first = StatisticsReport(153, 245);
        var second = StatisticsReport(153, 245);
        for (var index = 0; index < 8; index++)
        {
            var offset = 13 + 29 * index;
            first[offset] = (byte)(index + 1);
            BinaryPrimitives.WriteUInt16LittleEndian(first.AsSpan(offset + 1), (ushort)(1000 + index));
            BinaryPrimitives.WriteUInt16LittleEndian(first.AsSpan(offset + 3), (ushort)(2000 + index));
            BinaryPrimitives.WriteUInt16LittleEndian(first.AsSpan(offset + 5), (ushort)(20 + index));
            BinaryPrimitives.WriteUInt16LittleEndian(first.AsSpan(offset + 27), (ushort)(30 + index));
        }
        second[13] = 24;
        BinaryPrimitives.WriteUInt16LittleEndian(second.AsSpan(14), 1234);
        BinaryPrimitives.WriteUInt16LittleEndian(second.AsSpan(36), 4321);
        BinaryPrimitives.WriteUInt16LittleEndian(second.AsSpan(38), 65535);
        BinaryPrimitives.WriteUInt16LittleEndian(second.AsSpan(40), 321);

        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(KnownId, first));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Single(await NewReports());
            await link.Stream.WriteAsync(Message(KnownId, second));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(2, (await NewReports()).Count);
            await link.Stream.WriteAsync(HeaderOnlyRetransmission(KnownId, 0));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ReadFrameAsync(link.Stream, TimeSpan.FromMilliseconds(400)));
        }
        await using (var replay = await OpenAsync())
        {
            await replay.Stream.WriteAsync(Message(KnownId, first));
            AssertAck(await ReadFrameAsync(replay.Stream, TimeSpan.FromSeconds(5)));
        }

        var rows = await NewReports();
        Assert.Equal(2, rows.Count);
        var reports = new List<JsonElement>();
        foreach (var row in rows)
        {
            Assert.Equal("Graphical usage statistics", row.GetProperty("statisticsName").GetString());
            var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/uploads/{row.GetProperty("id").GetGuid()}", Json);
            var record = Assert.Single(detail.GetProperty("records").EnumerateArray());
            Assert.Equal("", record.GetProperty("rawPayloadHex").GetString());
            var report = record.GetProperty("terminalStatistics");
            Assert.Equal("2028-02-29T10:11:12", report.GetProperty("periodStart").GetString());
            Assert.Equal("2028-03-01T10:11:12", report.GetProperty("periodEnd").GetString());
            reports.Add(report);
        }
        var full = reports.Single(report => report.GetProperty("graphicalScreens").GetArrayLength() == 8);
        var fullLast = full.GetProperty("graphicalScreens")[7];
        Assert.Equal(8, fullLast.GetProperty("screenNumber").GetInt32());
        Assert.Equal(1007, fullLast.GetProperty("ownerCode").GetInt32());
        Assert.Equal(27, fullLast.GetProperty("keys")[0].GetProperty("pressCount").GetInt32());
        Assert.Equal(37, fullLast.GetProperty("directionKeyPressCount").GetInt32());
        var partial = Assert.Single(reports.Single(report => report.GetProperty("graphicalScreens").GetArrayLength() == 1)
            .GetProperty("graphicalScreens").EnumerateArray());
        Assert.Equal(24, partial.GetProperty("screenNumber").GetInt32());
        Assert.Equal(1234, partial.GetProperty("ownerCode").GetInt32());
        Assert.Equal(4321, partial.GetProperty("keys")[5].GetProperty("ownerCode").GetInt32());
        Assert.Equal(65535, partial.GetProperty("keys")[5].GetProperty("pressCount").GetInt32());
        Assert.Equal(321, partial.GetProperty("directionKeyPressCount").GetInt32());
        Assert.True(tableBatches.SetEquals(await BatchIdsAsync()));

        async Task<List<JsonElement>> NewReports()
        {
            var uploads = await fx.Client.GetFromJsonAsync<JsonElement>("/api/uploads", Json);
            return uploads.EnumerateArray().Where(row => !existing.Contains(row.GetProperty("id").GetGuid()) &&
                row.GetProperty("isTerminalStatistics").GetBoolean() &&
                row.GetProperty("statisticsMessageType").GetInt32() == 153).ToList();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Graphical_usage_from_an_unregistered_phone_or_truncated_is_not_acknowledged(bool truncated)
    {
        var report = StatisticsReport(153, 245);
        report[13] = 1;
        report[14] = 1;
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(truncated ? KnownId : UnknownId, truncated ? report[..^1] : report));
        await Assert.ThrowsAsync<EndOfStreamException>(() => ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
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
    public async Task Quick_access_clear_and_edit_preserve_spare_bytes_through_ncc_pickup()
    {
        using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = "Quick-access fixture" }, Json);
        created.EnsureSuccessStatusCode();
        var setId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        using var definition = await fx.Client.PostAsJsonAsync("/api/tables/definitions",
            new { name = "Quick-access fixture record", tableNumber = 55 }, Json);
        definition.EnsureSuccessStatusCode();
        var definitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var bytes = new byte[571];
        bytes[0] = 55;
        const int sixth = 286;
        const int tenth = 514;
        bytes[sixth] = 0x59;
        bytes[sixth + 3] = 0x12;
        bytes[sixth + 51] = 0x21;
        bytes[sixth + 55] = 0xFB;
        bytes[sixth + 56] = 0xA7;
        bytes[tenth - 1] = 0x40;
        using var added = await fx.Client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = setId, tableDefinitionId = definitionId, tableRevision = 1,
            payloadBase64 = Convert.ToBase64String(bytes), sortOrder = 0
        }, Json);
        added.EnsureSuccessStatusCode();
        var path = $"/api/tables/sets/{setId}/records/55/fields";
        var original = await fx.Client.GetFromJsonAsync<JsonElement>(path, Json);
        var sha256 = original.GetProperty("sha256").GetString();
        using var unchanged = await fx.Client.PutAsJsonAsync(path,
            new { revision = 1, sha256, changes = new Dictionary<string, string>() }, Json);
        Assert.Equal(HttpStatusCode.NoContent, unchanged.StatusCode);
        Assert.Equal(sha256, (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json)).GetProperty("sha256").GetString());
        using var saved = await fx.Client.PutAsJsonAsync(path, new
        {
            revision = 1, sha256, clearQuickAccessKeys = new[] { 6 },
            changes = new Dictionary<string, string> { ["key-9.customerReference"] = "258", ["key-9.number"] = "10" }
        }, Json);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var published = await fx.Client.PostAsync($"/api/tables/sets/{setId}/publish?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);

        await AssignSetAsync(setId);
        try
        {
            var before = await BatchIdsAsync();
            await using var link = await OpenAsync();
            await link.Stream.WriteAsync(Message(KnownId, [44, 2]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var header = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 18 }, header.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.PacketId(header.Control))));
            var record = new List<byte>();
            while (record.Count < bytes.Length)
            {
                var fragment = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                record.AddRange(fragment.Data);
                await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.PacketId(fragment.Control))));
            }
            bytes[sixth] = 0x18;
            bytes[sixth + 3] = 0;
            bytes[sixth + 51] = 0;
            bytes[sixth + 55] = 0xF3;
            bytes[sixth + 56] = 0xA6;
            bytes[tenth + 3] = 0x1A;
            bytes[tenth + 51] = 2;
            bytes[tenth + 52] = 1;
            Assert.Equal(bytes, record);
            await link.Stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket
            {
                Control = 1, TerminalId = KnownId, Data = [14, 55]
            }));
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
            var cancelledBatch = batchSchedule.GetProperty("lastBatchId").GetGuid();
            await WaitBatchAsync(cancelledBatch, 4);
            Assert.True(before.Add(cancelledBatch));
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
    public async Task Firmware_schedules_match_wire_id_ranges_and_recheck_the_latest_ncc_software_version()
    {
        var site = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/sites", Json))[0].GetProperty("id").GetGuid();
        const string firstWireId = "0000000801";
        const string secondWireId = "0000000802";
        var first = await AddPhoneAsync(firstWireId, "First version fixture");
        var second = await AddPhoneAsync(secondWireId, "Second version fixture");
        var unreported = await AddPhoneAsync("0000000803", "No software report");
        await AddPhoneAsync("000000080A", "Nondecimal wire ID");
        await AddPhoneAsync("0000000900", "Outside the ID range");

        using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = "Firmware selection test" }, Json);
        created.EnsureSuccessStatusCode();
        var setId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var setPath = $"/api/tables/sets/{setId}";
        var fieldsPath = $"{setPath}/records/94/fields";
        var record = await fx.Client.GetFromJsonAsync<JsonElement>(fieldsPath, Json);
        using var initialized = await fx.Client.PostAsJsonAsync(fieldsPath, record.GetProperty("templates")[0], Json);
        initialized.EnsureSuccessStatusCode();
        using var published = await fx.Client.PostAsync($"{setPath}/publish?confirm=true", null);
        published.EnsureSuccessStatusCode();

        var range = $"{setPath}/assignment-preview?targetType=terminal-id-range";
        var matches = await fx.Client.GetFromJsonAsync<JsonElement>(
            $"{range}&firmwareTarget.terminalIdFrom=0000000801&firmwareTarget.terminalIdTo=0000000899", Json);
        Assert.True(new[] { first, second, unreported }.ToHashSet().SetEquals(MatchedIds(matches)));
        var exact = await fx.Client.GetFromJsonAsync<JsonElement>(
            $"{range}&firmwareTarget.terminalIdFrom=0000000802&firmwareTarget.terminalIdTo=0000000802", Json);
        Assert.Equal(second, Assert.Single(MatchedIds(exact)));
        using var reversed = await fx.Client.GetAsync(
            $"{range}&firmwareTarget.terminalIdFrom=0000000802&firmwareTarget.terminalIdTo=0000000801");
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);

        var firmwareTarget = new { currentControlRom = "LABROM", currentControlSoftware = "A002" };
        var byVersion = "targetType=software-version&firmwareTarget.currentControlRom=LABROM&firmwareTarget.currentControlSoftware=A002";
        var schedulePath = $"{setPath}/schedules";
        await ReportVersionAsync(firstWireId, "A002");
        await ReportVersionAsync(secondWireId, "A003");
        Assert.Equal(first, Assert.Single(MatchedIds(await fx.Client.GetFromJsonAsync<JsonElement>($"{setPath}/assignment-preview?{byVersion}", Json))));

        await ReportVersionAsync(firstWireId, "A003");
        await ReportVersionAsync(secondWireId, "A002");
        using var stale = await fx.Client.PostAsJsonAsync(schedulePath,
            new { targetType = "software-version", firmwareTarget, terminalIds = new[] { first } }, Json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(second, Assert.Single(MatchedIds(await fx.Client.GetFromJsonAsync<JsonElement>($"{setPath}/assignment-preview?{byVersion}", Json))));

        using var scheduled = await fx.Client.PostAsJsonAsync(schedulePath,
            new { targetType = "software-version", firmwareTarget, terminalIds = new[] { second } }, Json);
        scheduled.EnsureSuccessStatusCode();
        var queued = await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json);
        var previousSchedule = Assert.Single(queued.EnumerateArray());
        Assert.Equal(second, previousSchedule.GetProperty("terminalId").GetGuid());
        var previousScheduleId = previousSchedule.GetProperty("id").GetGuid();
        var previousBatchId = previousSchedule.GetProperty("lastBatchId").GetGuid();

        using var clone = await fx.Client.PostAsJsonAsync($"{setPath}/clone", new { name = "Revised firmware selection test" }, Json);
        clone.EnsureSuccessStatusCode();
        var revisedId = (await clone.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var revisedPath = $"/api/tables/sets/{revisedId}";
        var revisedFieldPath = $"{revisedPath}/records/94/fields";
        var revisedFields = await fx.Client.GetFromJsonAsync<JsonElement>(revisedFieldPath, Json);
        var changes = new Dictionary<string, string> { ["accessMinutes"] = "17" };
        using var edited = await fx.Client.PutAsJsonAsync(revisedFieldPath, new
        {
            profile = revisedFields.GetProperty("profile"), revision = revisedFields.GetProperty("revision"),
            sha256 = revisedFields.GetProperty("sha256"), changes
        }, Json);
        edited.EnsureSuccessStatusCode();
        using var revisedPublished = await fx.Client.PostAsync($"{revisedPath}/publish?confirm=true", null);
        revisedPublished.EnsureSuccessStatusCode();
        var revisedPreview = (await fx.Client.GetFromJsonAsync<JsonElement>(
            $"{revisedPath}/assignment-preview?{byVersion}&allowFirmwareReschedule=true", Json)).GetProperty("targets")[0];
        Assert.True(revisedPreview.GetProperty("eligible").GetBoolean());
        Assert.Equal(previousScheduleId, revisedPreview.GetProperty("replacesFirmwareScheduleId").GetGuid());
        using var replacement = await fx.Client.PostAsJsonAsync($"{revisedPath}/schedules", new
        {
            targetType = "software-version", firmwareTarget, terminalIds = new[] { second },
            allowFirmwareReschedule = true, replacesFirmwareScheduleIds = new[] { previousScheduleId }
        }, Json);
        replacement.EnsureSuccessStatusCode();
        Assert.Equal("Cancelled", (await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json))[0].GetProperty("status").GetString());
        await WaitBatchAsync(previousBatchId, 4);
        var revisedBatchId = (await fx.Client.GetFromJsonAsync<JsonElement>($"{revisedPath}/schedules", Json))[0].GetProperty("lastBatchId").GetGuid();
        var template = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(94)));
        var expected = NativeTableLayouts.Find(94, template)!.Apply(template, changes);
        await using (var callIn = await OpenAsync())
        {
            var wire = Convert.FromHexString(secondWireId);
            await callIn.Stream.WriteAsync(Message(wire, [8]));
            AssertAck(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            var header = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 18 }, header.Data);
            await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
            var parameters = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(expected, parameters.Data);
            await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | parameters.Control)));
            await callIn.Stream.WriteAsync(Message(wire, [14, 94]));
            AssertAck(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            var end = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, end.Data);
            await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
            await WaitBatchAsync(revisedBatchId, 2);
        }

        var dataSet = await CreateRateTableSetAsync("Firmware scope rejects data sets", new byte[1192]);
        using var dataPreview = await fx.Client.GetAsync($"/api/tables/sets/{dataSet}/assignment-preview?{byVersion}");
        Assert.Equal(HttpStatusCode.BadRequest, dataPreview.StatusCode);

        static IEnumerable<Guid> MatchedIds(JsonElement preview) =>
            preview.GetProperty("targets").EnumerateArray().Select(target => target.GetProperty("id").GetGuid());

        async Task<Guid> AddPhoneAsync(string wireId, string name)
        {
            using var response = await fx.Client.PostAsJsonAsync("/api/terminals",
                new { siteId = site, displayName = name, terminalIdHex = wireId, status = 0 }, Json);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        }

        async Task ReportVersionAsync(string wireId, string software)
        {
            var terminalId = Convert.FromHexString(wireId);
            var bundle = new byte[6 + 11 + 28 + 1];
            bundle[0] = 6;
            bundle[6] = 10;
            bundle[17] = 60;
            Encoding.ASCII.GetBytes("LABROM ").CopyTo(bundle, 18);
            Encoding.ASCII.GetBytes(software).CopyTo(bundle, 25);
            bundle[^1] = 13;
            await using var link = await OpenAsync();
            await link.Stream.WriteAsync(Message(terminalId, bundle));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var receipt = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 15, 0, 0, 13 }, receipt.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | receipt.Control)));
        }
    }

    [Fact]
    public async Task Firmware_window_schedules_distinct_immutable_records_and_delivers_each_allocated_start()
    {
        var site = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/sites", Json))[0].GetProperty("id").GetGuid();
        string[] wireIds = ["0000000911", "0000000912", "0000000913"];
        var phones = new List<Guid>();
        foreach (var wire in wireIds)
        {
            using var created = await fx.Client.PostAsJsonAsync("/api/terminals",
                new { siteId = site, displayName = "Firmware window fixture " + phones.Count, terminalIdHex = wire, status = 0 }, Json);
            created.EnsureSuccessStatusCode();
            phones.Add((await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid());
        }

        using var setResponse = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = "Firmware window fixture" }, Json);
        setResponse.EnsureSuccessStatusCode();
        var source = (await setResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var path = $"/api/tables/sets/{source}";
        var fieldPath = $"{path}/records/94/fields";
        var missing = await fx.Client.GetFromJsonAsync<JsonElement>(fieldPath, Json);
        using var initialized = await fx.Client.PostAsJsonAsync(fieldPath, missing.GetProperty("templates")[0], Json);
        initialized.EnsureSuccessStatusCode();
        var fields = await fx.Client.GetFromJsonAsync<JsonElement>(fieldPath, Json);
        var date = DateOnly.FromDateTime(fx.Factory.Clock.GetUtcNow().UtcDateTime.AddDays(1));
        var settings = new Dictionary<string, string>
        {
            ["enabled"] = "true", ["startAt"] = date.ToDateTime(new TimeOnly(1, 0)).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["server"] = "1234", ["targetRom"] = "ROMTEST", ["targetApp"] = "A001",
            ["sector-0"] = "true", ["sector-31"] = "true", ["sector-255"] = "true"
        };
        using var saved = await fx.Client.PutAsJsonAsync(fieldPath, new
        {
            profile = fields.GetProperty("profile"), revision = fields.GetProperty("revision"), sha256 = fields.GetProperty("sha256"), changes = settings
        }, Json);
        saved.EnsureSuccessStatusCode();
        using var published = await fx.Client.PostAsync($"{path}/publish?confirm=true", null);
        published.EnsureSuccessStatusCode();
        var template = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(94)));
        var original = NativeTableLayouts.Find(94, template)!.Apply(template, settings);
        var target = new { terminalIdFrom = wireIds[0], terminalIdTo = wireIds[^1] };
        var window = new { startDate = date, dailyStart = new TimeOnly(2, 0), dailyEnd = new TimeOnly(2, 30), activeServers = 2, slotMinutes = 30 };
        var windowParams = $"&firmwareWindow.startDate={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&firmwareWindow.dailyStart=02%3A00&firmwareWindow.dailyEnd=02%3A30" +
            "&firmwareWindow.activeServers=2&firmwareWindow.slotMinutes=30";
        var previewUrl = $"{path}/assignment-preview?targetType=terminal-id-range&firmwareTarget.terminalIdFrom={wireIds[0]}&firmwareTarget.terminalIdTo={wireIds[^1]}{windowParams}";
        var preview = await fx.Client.GetFromJsonAsync<JsonElement>(previewUrl, Json);
        var allocation = preview.GetProperty("firmwareAllocation");
        Assert.Equal(3, allocation.GetProperty("selectedBlocks").GetInt32());
        var first = date.ToDateTime(new TimeOnly(2, 0), DateTimeKind.Utc);
        var starts = new[] { first, first, first.AddDays(1) };
        var targets = preview.GetProperty("targets").EnumerateArray().ToDictionary(row => row.GetProperty("id").GetGuid());
        for (var i = 0; i < phones.Count; i++)
            Assert.Equal(starts[i], targets[phones[i]].GetProperty("firmwareStartAtUtc").GetDateTime());

        using var stale = await fx.Client.PostAsJsonAsync($"{path}/schedules", new
        {
            targetType = "terminal-id-range", firmwareTarget = target, terminalIds = phones, firmwareWindow = window, firmwarePreviewToken = "stale"
        }, Json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Empty((await fx.Client.GetFromJsonAsync<JsonElement>($"{path}/schedules", Json)).EnumerateArray());
        using var scheduled = await fx.Client.PostAsJsonAsync($"{path}/schedules", new
        {
            targetType = "terminal-id-range", firmwareTarget = target, terminalIds = phones,
            firmwareWindow = window, firmwarePreviewToken = allocation.GetProperty("previewToken").GetString()
        }, Json);
        scheduled.EnsureSuccessStatusCode();
        var schedules = (await fx.Client.GetFromJsonAsync<JsonElement>($"{path}/schedules", Json)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("terminalId").GetGuid());
        Assert.Equal(schedules[phones[0]].GetProperty("tableSetId").GetGuid(), schedules[phones[1]].GetProperty("tableSetId").GetGuid());
        Assert.NotEqual(schedules[phones[0]].GetProperty("tableSetId").GetGuid(), schedules[phones[2]].GetProperty("tableSetId").GetGuid());
        for (var i = 0; i < phones.Count; i++)
        {
            var row = schedules[phones[i]];
            Assert.Equal(source, row.GetProperty("sourceTableSetId").GetGuid());
            Assert.Equal(starts[i], row.GetProperty("firmwareStartAtUtc").GetDateTime());
            Assert.Equal(starts[i].AddMinutes(30), row.GetProperty("firmwareSlotEndAtUtc").GetDateTime());
            await DeliverAsync(wireIds[i], CodeDownloadWire.WithStart(original, starts[i]), row.GetProperty("lastBatchId").GetGuid());
        }
        var stored = await fx.Client.GetFromJsonAsync<JsonElement>(fieldPath, Json);
        Assert.Equal(settings["startAt"], stored.GetProperty("sections")[0].GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("key").GetString() == "startAt").GetProperty("value").GetString());
        var delivered = (await fx.Client.GetFromJsonAsync<JsonElement>($"{path}/schedules", Json)).EnumerateArray();
        Assert.All(delivered, row => Assert.Equal("Delivered", row.GetProperty("status").GetString()));

        const string nextWireId = "0000000914";
        using var nextPhoneResponse = await fx.Client.PostAsJsonAsync("/api/terminals",
            new { siteId = site, displayName = "Later firmware window fixture", terminalIdHex = nextWireId, status = 0 }, Json);
        nextPhoneResponse.EnsureSuccessStatusCode();
        var nextPhone = (await nextPhoneResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var nextPreviewUrl = $"{path}/assignment-preview?targetType=terminal&targetId={nextPhone}{windowParams}";
        var reserved = await fx.Client.GetFromJsonAsync<JsonElement>(nextPreviewUrl, Json);
        Assert.Equal(3, reserved.GetProperty("firmwareAllocation").GetProperty("existingReservations").GetInt32());
        Assert.Equal(first.AddDays(1), reserved.GetProperty("targets")[0].GetProperty("firmwareStartAtUtc").GetDateTime());
        Assert.Equal(2, reserved.GetProperty("targets")[0].GetProperty("firmwareServerSlot").GetInt32());

        var lastScheduleId = schedules[phones[^1]].GetProperty("id").GetGuid();
        using var cleared = await fx.Client.PostAsync($"{path}/schedules/{lastScheduleId}/clear-firmware", null);
        cleared.EnsureSuccessStatusCode();
        var clearing = await cleared.Content.ReadFromJsonAsync<JsonElement>(Json);
        var pendingClear = await fx.Client.GetFromJsonAsync<JsonElement>(nextPreviewUrl, Json);
        Assert.Equal(3, pendingClear.GetProperty("firmwareAllocation").GetProperty("existingReservations").GetInt32());
        await DeliverAsync(wireIds[^1], CodeDownloadWire.Disable(CodeDownloadWire.WithStart(original, starts[^1])),
            clearing.GetProperty("batchId").GetGuid());
        var history = (await fx.Client.GetFromJsonAsync<JsonElement>($"{path}/schedules", Json)).EnumerateArray().ToArray();
        Assert.Equal(4, history.Length);
        var clearRow = history.Single(row => row.GetProperty("id").GetGuid() == clearing.GetProperty("scheduleId").GetGuid());
        Assert.Equal(source, clearRow.GetProperty("sourceTableSetId").GetGuid());
        Assert.Equal("Delivered", clearRow.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, clearRow.GetProperty("firmwareStartAtUtc").ValueKind);

        var released = await fx.Client.GetFromJsonAsync<JsonElement>(nextPreviewUrl, Json);
        Assert.Equal(2, released.GetProperty("firmwareAllocation").GetProperty("existingReservations").GetInt32());
        Assert.Equal(1, released.GetProperty("targets")[0].GetProperty("firmwareServerSlot").GetInt32());
        using var staleCapacity = await fx.Client.PostAsJsonAsync($"{path}/schedules", new
        {
            targetType = "terminal", targetId = nextPhone, terminalIds = new[] { nextPhone }, firmwareWindow = window,
            firmwarePreviewToken = reserved.GetProperty("firmwareAllocation").GetProperty("previewToken").GetString()
        }, Json);
        Assert.Equal(HttpStatusCode.Conflict, staleCapacity.StatusCode);
        using var queuedNextJob = await fx.Client.PostAsJsonAsync($"{path}/schedules", new
        {
            targetType = "terminal", targetId = nextPhone, terminalIds = new[] { nextPhone }, firmwareWindow = window,
            firmwarePreviewToken = released.GetProperty("firmwareAllocation").GetProperty("previewToken").GetString()
        }, Json);
        queuedNextJob.EnsureSuccessStatusCode();
        var nextJob = (await fx.Client.GetFromJsonAsync<JsonElement>($"{path}/schedules", Json)).EnumerateArray()
            .Single(row => row.GetProperty("terminalId").GetGuid() == nextPhone);
        await DeliverAsync(nextWireId, CodeDownloadWire.WithStart(original, first.AddDays(1)), nextJob.GetProperty("lastBatchId").GetGuid());

        async Task DeliverAsync(string wireId, byte[] expected, Guid batchId)
        {
            await using var callIn = await OpenAsync();
            var wire = Convert.FromHexString(wireId);
            await callIn.Stream.WriteAsync(Message(wire, [8]));
            AssertAck(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            var header = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 18 }, header.Data);
            await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
            var parameters = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(expected, parameters.Data);
            await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | parameters.Control)));
            await callIn.Stream.WriteAsync(Message(wire, [14, 94]));
            AssertAck(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            var end = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, end.Data);
            await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
            await WaitBatchAsync(batchId, 2);
        }
    }

    [Fact]
    public async Task Time_area_firmware_schedule_delivers_mt94_on_call_in_and_keeps_the_table_assignment()
    {
        await AssignRateTableSetAsync("Firmware parameter baseline data set");
        var terminals = await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json);
        var terminalId = terminals.EnumerateArray().Single(t => t.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId))
            .GetProperty("id").GetGuid();
        var assignmentPath = $"/api/terminals/{terminalId}/table-assignment";
        var originalAssignment = (await fx.Client.GetFromJsonAsync<JsonElement>(assignmentPath, Json)).GetProperty("tableSetId").GetGuid();
        var inventoryPath = $"/api/terminals/{terminalId}/inventory";
        var inventory = await fx.Client.GetFromJsonAsync<JsonElement>(inventoryPath, Json);
        var profile = inventory.GetProperty("profile").Deserialize<Dictionary<string, JsonElement>>()!;
        profile["timeAreaCode"] = JsonSerializer.SerializeToElement("e7");
        using var timeArea = await fx.Client.PutAsJsonAsync(inventoryPath, new { profile, revision = inventory.GetProperty("revision") }, Json);
        timeArea.EnsureSuccessStatusCode();
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
            ["serverAuthentication"] = Convert.ToHexString(Enumerable.Range(0, 6).Select(index => (byte)(0xA0 + index)).ToArray()),
            ["spare.byte1"] = "165", ["spare.byte26"] = "129"
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
        var selection = new { targetType = "time-area", timeAreaCode = "E7", terminalIds = new[] { terminalId } };
        var preview = await fx.Client.GetFromJsonAsync<JsonElement>($"{setPath}/assignment-preview?targetType=time-area&timeAreaCode=e7", Json);
        Assert.Equal(terminalId, Assert.Single(preview.GetProperty("targets").EnumerateArray()).GetProperty("id").GetGuid());
        using var assignment = await fx.Client.PostAsJsonAsync($"{setPath}/assignments", selection, Json);
        Assert.Equal(HttpStatusCode.BadRequest, assignment.StatusCode);
        using var direct = await fx.Client.PostAsJsonAsync(assignmentPath, new { tableSetId = setId }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);
        var schedulePath = $"{setPath}/schedules";
        using var late = await fx.Client.PostAsJsonAsync(schedulePath,
            new { selection.targetType, selection.timeAreaCode, selection.terminalIds, availableAtUtc = start.AddMinutes(1) }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        using var scheduled = await fx.Client.PostAsJsonAsync(schedulePath, selection, Json);
        scheduled.EnsureSuccessStatusCode();
        var prepared = (await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json))[0];
        var scheduleId = prepared.GetProperty("id").GetGuid();
        var preparedBatchId = prepared.GetProperty("lastBatchId").GetGuid();
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
            Assert.Equal(165, expected[74]); Assert.Equal(129, expected[99]);
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
                await WaitBatchAsync(preparedBatchId, 2);
            }
            Assert.Equal("Delivered", (await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json))[0].GetProperty("status").GetString());
            Assert.Equal(originalAssignment, (await fx.Client.GetFromJsonAsync<JsonElement>(assignmentPath, Json)).GetProperty("tableSetId").GetGuid());

            var delivered = (await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json))[0];
            Assert.True(delivered.GetProperty("canQueueFirmwareCancellation").GetBoolean());
            var cancellationPath = $"{schedulePath}/{scheduleId}/clear-firmware";
            using var cleared = await fx.Client.PostAsync(cancellationPath, null);
            cleared.EnsureSuccessStatusCode();
            var cancellation = await cleared.Content.ReadFromJsonAsync<JsonElement>(Json);
            var cancellationBatch = cancellation.GetProperty("batchId").GetGuid();
            var cancellationSet = cancellation.GetProperty("tableSetId").GetGuid();
            var cancellationSchedule = cancellation.GetProperty("scheduleId").GetGuid();
            var cancellationSchedulesPath = $"/api/tables/sets/{cancellationSet}/schedules";
            using var duplicateClear = await fx.Client.PostAsync(cancellationPath, null);
            Assert.Equal(HttpStatusCode.Conflict, duplicateClear.StatusCode);
            Assert.False((await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath, Json))[0]
                .GetProperty("canQueueFirmwareCancellation").GetBoolean());
            Assert.Equal("Ready", (await fx.Client.GetFromJsonAsync<JsonElement>(cancellationSchedulesPath, Json))[0]
                .GetProperty("status").GetString());
            var disabled = expected.ToArray();
            disabled[1] &= 0xFE;
            try
            {
                await using (var callIn = await OpenAsync())
                {
                    await callIn.Stream.WriteAsync(Message(KnownId, [8]));
                    AssertAck(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
                    var header = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
                    Assert.Equal(new byte[] { 18 }, header.Data);
                    await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
                    var parameters = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
                    Assert.Equal(disabled, parameters.Data);
                    await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | parameters.Control)));
                    await callIn.Stream.WriteAsync(Message(KnownId, [14, 94]));
                    AssertAck(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
                    var end = Decode(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
                    Assert.Equal(new byte[] { 13 }, end.Data);
                    await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
                    await WaitBatchAsync(cancellationBatch, 2);
                }
                var cancellationDelivered = (await fx.Client.GetFromJsonAsync<JsonElement>(cancellationSchedulesPath, Json))[0];
                Assert.Equal("Delivered", cancellationDelivered.GetProperty("status").GetString());
                Assert.False(cancellationDelivered.GetProperty("canQueueFirmwareCancellation").GetBoolean());
                using var staleClear = await fx.Client.PostAsync(cancellationPath, null);
                Assert.Equal(HttpStatusCode.Conflict, staleClear.StatusCode);
                Assert.Equal(originalAssignment, (await fx.Client.GetFromJsonAsync<JsonElement>(assignmentPath, Json)).GetProperty("tableSetId").GetGuid());
            }
            finally
            {
                using var cleanupCancellation = await fx.Client.PostAsync($"{cancellationSchedulesPath}/{cancellationSchedule}/cancel", null);
            }

            var existingPreview = (await fx.Client.GetFromJsonAsync<JsonElement>(
                $"{setPath}/assignment-preview?targetType=time-area&timeAreaCode=e7", Json)).GetProperty("targets")[0];
            Assert.False(existingPreview.GetProperty("eligible").GetBoolean());
            Assert.Contains("Existing firmware schedule", existingPreview.GetProperty("reason").GetString());
            using var keepExisting = await fx.Client.PostAsJsonAsync(schedulePath, selection, Json);
            Assert.Equal(HttpStatusCode.Conflict, keepExisting.StatusCode);
            var replacementPreview = (await fx.Client.GetFromJsonAsync<JsonElement>(
                $"{setPath}/assignment-preview?targetType=time-area&timeAreaCode=e7&allowFirmwareReschedule=true", Json))
                .GetProperty("targets")[0];
            Assert.True(replacementPreview.GetProperty("eligible").GetBoolean());
            Assert.Equal(cancellationSchedule, replacementPreview.GetProperty("replacesFirmwareScheduleId").GetGuid());
            using var rescheduled = await fx.Client.PostAsJsonAsync(schedulePath, new
            {
                selection.targetType, selection.timeAreaCode, selection.terminalIds,
                allowFirmwareReschedule = true, replacesFirmwareScheduleIds = new[] { cancellationSchedule }
            }, Json);
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
            await WaitBatchAsync(expiredSchedule.GetProperty("lastBatchId").GetGuid(), 4);
        }
        finally
        {
            fx.Factory.Clock.Advance(TimeSpan.Zero);
            using var cleanup = await fx.Client.PostAsync($"{schedulePath}/{scheduleId}/cancel", null);
            await AssignLabSetAsync();
        }
    }

    [Fact]
    public async Task Time_area_call_in_parameters_deliver_mt35_as_partial_without_an_assignment()
    {
        var seed = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json)).EnumerateArray()
            .Single(t => t.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId));
        const string wireId = "0000000629";
        using var phone = await fx.Client.PostAsJsonAsync("/api/terminals", new
        {
            siteId = seed.GetProperty("siteId"), terminalIdHex = wireId, displayName = "Call-in area fixture", status = 0
        }, Json);
        phone.EnsureSuccessStatusCode();
        var terminalId = (await phone.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        using var inventory = await fx.Client.PutAsJsonAsync($"/api/terminals/{terminalId}/inventory",
            new { profile = new { timeAreaCode = "+1" } }, Json);
        inventory.EnsureSuccessStatusCode();
        var areas = await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals/time-areas", Json);
        Assert.Equal(1, areas.EnumerateArray().Single(area => area.GetProperty("id").GetString() == "+1")
            .GetProperty("phoneCount").GetInt32());

        using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = "Area call-in parameter set" }, Json);
        created.EnsureSuccessStatusCode();
        var setId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var endpoint = $"/api/tables/sets/{setId}";
        var fields = $"{endpoint}/records/35/fields";
        var templates = await fx.Client.GetFromJsonAsync<JsonElement>(fields, Json);
        using var saved = await fx.Client.PostAsJsonAsync(fields, templates.GetProperty("templates")[0], Json);
        saved.EnsureSuccessStatusCode();
        using var published = await fx.Client.PostAsync($"{endpoint}/publish?confirm=true", null);
        published.EnsureSuccessStatusCode();
        var scoped = new { targetType = "time-area", timeAreaCode = "+1", terminalIds = new[] { terminalId } };
        var preview = await fx.Client.GetFromJsonAsync<JsonElement>(
            $"{endpoint}/assignment-preview?targetType=time-area&timeAreaCode=%2B1", Json);
        Assert.Equal(terminalId, Assert.Single(preview.GetProperty("targets").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(new[] { 35 }, preview.GetProperty("recordNumbers").Deserialize<int[]>());
        using var forbidden = await fx.Client.PostAsJsonAsync($"{endpoint}/assignments", scoped, Json);
        Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
        var unrelatedSet = await CreateRateTableSetAsync("Area schedule invalid record");
        using var invalid = await fx.Client.PostAsJsonAsync($"/api/tables/sets/{unrelatedSet}/schedules", scoped, Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var scheduled = await fx.Client.PostAsJsonAsync($"{endpoint}/schedules", scoped, Json);
        scheduled.EnsureSuccessStatusCode();
        var prepared = (await fx.Client.GetFromJsonAsync<JsonElement>($"{endpoint}/schedules", Json))[0];
        var preparedBatchId = prepared.GetProperty("lastBatchId").GetGuid();

        var wire = Convert.FromHexString(wireId);
        await using var link = await OpenAsync();
        await link.Stream.WriteAsync(Message(wire, [8]));
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        var header = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(new byte[] { 18 }, header.Data);
        await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
        var record = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(21, record.Data.Length);
        Assert.Equal(35, record.Data[0]);
        await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | record.Control)));
        await link.Stream.WriteAsync(Message(wire, [14, 35]));
        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        var end = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
        Assert.Equal(new byte[] { 13 }, end.Data);
        await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
        await WaitBatchAsync(preparedBatchId, 2);
        using var assignment = await fx.Client.GetAsync($"/api/terminals/{terminalId}/table-assignment");
        Assert.Equal(HttpStatusCode.NotFound, assignment.StatusCode);
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
        Assert.Equal(JsonValueKind.Null, schedule.GetProperty("lastBatchId").ValueKind);
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
            var prepared = await WaitNewBatchAsync(before, 0);
            var preparedId = prepared.GetProperty("id").GetGuid();
            Assert.Equal(scheduleId, prepared.GetProperty("scheduleId").GetGuid());
            Assert.Equal(JsonValueKind.Null, prepared.GetProperty("nccSessionId").ValueKind);
            Assert.True(before.Add(preparedId));
            schedule = (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json))[0];
            Assert.Equal("Ready", schedule.GetProperty("status").GetString());
            Assert.True(schedule.GetProperty("preparedAtUtc").GetDateTime() >= body.availableAtUtc.UtcDateTime);
            Assert.Equal(JsonValueKind.Null, schedule.GetProperty("startedAtUtc").ValueKind);
            using var cancelPrepared = await fx.Client.PostAsync($"/api/downloads/{preparedId}/cancel?confirm=true", null);
            Assert.Equal(HttpStatusCode.BadRequest, cancelPrepared.StatusCode);

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
                Assert.Equal(preparedId, activeBatchId);
                using var cancelActiveBatch = await fx.Client.PostAsync($"/api/downloads/{activeBatchId}/cancel?confirm=true", null);
                Assert.Equal(HttpStatusCode.BadRequest, cancelActiveBatch.StatusCode);

                await using var competing = await OpenAsync();
                await competing.Stream.WriteAsync(Message(KnownId, [8]));
                AssertAck(await ReadFrameAsync(competing.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 13 }, Decode(await ReadFrameAsync(competing.Stream, TimeSpan.FromSeconds(5))).Data);
            }
            await WaitBatchAsync(preparedId, 3);
            schedule = (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json))[0];
            Assert.Equal("Retry pending", schedule.GetProperty("status").GetString());
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
                var retry = await WaitNewBatchAsync(before, 2);
                Assert.Equal(1, retry.GetProperty("retryCount").GetInt32());
                Assert.Equal(scheduleId, retry.GetProperty("scheduleId").GetGuid());
            }

            schedule = (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json))[0];
            Assert.Equal("Delivered", schedule.GetProperty("status").GetString());
            using var retryOld = await fx.Client.PostAsync($"/api/downloads/{preparedId}/retry?confirm=true", null);
            Assert.Equal(HttpStatusCode.BadRequest, retryOld.StatusCode);
            var assignment = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/terminals/{terminalId}/table-assignment", Json);
            Assert.Equal(setId, assignment.GetProperty("tableSetId").GetGuid());
            Assert.Equal(previousSetId, assignment.GetProperty("previousTableSetId").GetGuid());

            var delivered = await BatchIdsAsync();
            using var another = await fx.Client.PostAsJsonAsync(path, new
            {
                body.targetType, body.targetId, body.terminalIds, availableAtUtc = (DateTimeOffset?)null
            }, Json);
            another.EnsureSuccessStatusCode();
            var newScheduledRow = (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json))[0];
            var newSchedule = newScheduledRow.GetProperty("id").GetGuid();
            var cancelledPreparedId = newScheduledRow.GetProperty("lastBatchId").GetGuid();
            Assert.True(delivered.Add(cancelledPreparedId));
            using var cancel = await fx.Client.PostAsync($"{path}/{newSchedule}/cancel", null);
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
            await WaitBatchAsync(cancelledPreparedId, 4);
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
    public async Task Later_compiled_profile_validates_untargeted_pickup_and_delivers_edited_records()
    {
        using var firmware = await fx.Client.PostAsJsonAsync("/api/firmware/versions",
            new { label = "Later transfer fixture", tableProfileId = FirmwareTableProfiles.LaterMultiPay180 }, Json);
        firmware.EnsureSuccessStatusCode();
        var firmwareId = (await firmware.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var seed = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json)).EnumerateArray()
            .Single(row => row.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId));
        byte[] wire = Convert.FromHexString("0000000621");
        using var created = await fx.Client.PostAsJsonAsync("/api/terminals", new
        {
            siteId = seed.GetProperty("siteId").GetGuid(), firmwareVersionId = firmwareId,
            terminalIdHex = Convert.ToHexString(wire), displayName = "Later transfer fixture", status = 0
        }, Json);
        created.EnsureSuccessStatusCode();
        var phone = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var wrong = new byte[35];
        wrong[0] = 33;
        var display = new byte[68]; display[0] = 30; display[^1] = 0xA5;
        var wrongSet = await CreateSet("Untargeted mixed records", null, wrong, display);
        await Publish(wrongSet);
        var before = await BatchIdsAsync();
        using (var rejected = await fx.Client.PostAsJsonAsync($"/api/terminals/{phone}/downloads",
            new { tableSetId = wrongSet, scope = "Full" }, Json))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("36 bytes", (await rejected.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("error").GetString());
        }
        Assert.Empty((await BatchIdsAsync()).Except(before));

        // Model a previously queued configuration whose installed firmware
        // was identified after it was queued. Pickup must still reject it.
        using (var unclassified = await fx.Client.PutAsJsonAsync($"/api/terminals/{phone}/firmware",
            new { firmwareVersionId = (Guid?)null }, Json)) unclassified.EnsureSuccessStatusCode();
        var wrongBatch = await Queue(wrongSet);
        using (var classified = await fx.Client.PutAsJsonAsync($"/api/terminals/{phone}/firmware",
            new { firmwareVersionId = firmwareId }, Json)) classified.EnsureSuccessStatusCode();
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(wire, [8]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var end = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, end.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
        }
        var refused = await WaitBatchAsync(wrongBatch, 3);
        Assert.Contains("36 bytes", refused.GetProperty("lastError").GetString());
        Assert.All(refused.GetProperty("timeline").EnumerateArray(), step =>
            Assert.Equal("not_sent", step.GetProperty("lastAckStatus").GetString()));
        using (var retry = await fx.Client.PostAsync($"/api/downloads/{wrongBatch}/retry?confirm=true", null))
            Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);

        var details = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{wrongSet}", Json);
        var displayDefinition = details.GetProperty("versions").EnumerateArray().Single(row => row.GetProperty("tableNumber").GetInt32() == 30)
            .GetProperty("tableDefinitionId").GetGuid();
        using var selected = await fx.Client.PostAsJsonAsync($"/api/terminals/{phone}/downloads",
            new { tableSetId = wrongSet, scope = "Partial", partialTableDefinitionIds = new[] { displayDefinition } }, Json);
        selected.EnsureSuccessStatusCode();
        var partial = (await selected.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(wire, [8]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var header = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 18 }, header.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
            var record = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(display, record.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | record.Control)));
            await link.Stream.WriteAsync(Message(wire, [14, 30]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var end = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, end.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
        }
        var selectedComplete = await WaitBatchAsync(partial, 2);
        Assert.Equal("ack", Assert.Single(selectedComplete.GetProperty("timeline").EnumerateArray()).GetProperty("lastAckStatus").GetString());

        var access = new byte[48];
        access[0] = 21;
        wire.CopyTo(access, 1);
        Array.Fill(access, (byte)0x5A, 34, 14);
        var modem = new byte[36];
        modem[0] = 33;
        modem[32] = 0x51;
        modem[33] = 0x80;
        modem[34] = 0x40;
        var screen = new byte[3061];
        screen[0] = 92;
        var validSet = await CreateSet("Later compatible records", firmwareId, access, modem, screen);
        await Edit(validSet, 21, new() { ["primary"] = "12345678901234567890" });
        Convert.FromHexString("123456789A123456789A").CopyTo(access, 6);
        await Edit(validSet, 33, new() { ["answerRemovalDelay"] = "4321", ["carrierLossTimeout"] = "true" });
        modem[12] = 0xE1;
        modem[13] = 0x10;
        modem[33] = 0x82;
        await Publish(validSet);
        var batch = await Queue(validSet);
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(wire, [8]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var header = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 18 }, header.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
            foreach (var expected in new[] { access, modem, screen })
            {
                var received = new List<byte>();
                while (received.Count < expected.Length)
                {
                    var fragment = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                    received.AddRange(fragment.Data);
                    await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | fragment.Control)));
                }
                Assert.Equal(expected, received);
                await link.Stream.WriteAsync(Message(wire, [14, expected[0]]));
                AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            }
            var end = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, end.Data);
            await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
        }
        var completed = await WaitBatchAsync(batch, 2);
        Assert.Equal(3, completed.GetProperty("timeline").GetArrayLength());
        Assert.All(completed.GetProperty("timeline").EnumerateArray(), step =>
            Assert.Equal("ack", step.GetProperty("lastAckStatus").GetString()));

        async Task<Guid> CreateSet(string name, Guid? target, params byte[][] records)
        {
            using var createdSet = await fx.Client.PostAsJsonAsync("/api/tables/sets",
                new { name, targetFirmwareVersionId = target }, Json);
            createdSet.EnsureSuccessStatusCode();
            var id = (await createdSet.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
            foreach (var record in records)
            {
                var existing = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/definitions", Json))
                    .EnumerateArray().FirstOrDefault(row => row.GetProperty("tableNumber").GetInt32() == record[0]);
                Guid definitionId;
                if (existing.ValueKind == JsonValueKind.Object)
                    definitionId = existing.GetProperty("id").GetGuid();
                else
                {
                    using var definition = await fx.Client.PostAsJsonAsync("/api/tables/definitions",
                        new { name = $"{name} MT{record[0]}", tableNumber = (int)record[0] }, Json);
                    definition.EnsureSuccessStatusCode();
                    definitionId = (await definition.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
                }
                using var imported = await fx.Client.PostAsJsonAsync("/api/tables/versions", new
                {
                    tableSetId = id, tableDefinitionId = definitionId,
                    tableRevision = 1, payloadBase64 = Convert.ToBase64String(record), sortOrder = (int)record[0]
                }, Json);
                imported.EnsureSuccessStatusCode();
            }
            return id;
        }

        async Task Edit(Guid id, int number, Dictionary<string, string> changes)
        {
            var path = $"/api/tables/sets/{id}/records/{number}/fields";
            var original = await fx.Client.GetFromJsonAsync<JsonElement>(path, Json);
            Assert.Equal(NativeTableLayouts.Later, original.GetProperty("profile").GetString());
            var sha256 = original.GetProperty("sha256").GetString();
            using var untouched = await fx.Client.PutAsJsonAsync(path,
                new { revision = 1, sha256, changes = new Dictionary<string, string>() }, Json);
            untouched.EnsureSuccessStatusCode();
            Assert.Equal(sha256, (await fx.Client.GetFromJsonAsync<JsonElement>(path, Json)).GetProperty("sha256").GetString());
            using var edited = await fx.Client.PutAsJsonAsync(path, new { revision = 1, sha256, changes }, Json);
            edited.EnsureSuccessStatusCode();
        }

        async Task Publish(Guid id)
        {
            using var response = await fx.Client.PostAsync($"/api/tables/sets/{id}/publish?confirm=true", null);
            response.EnsureSuccessStatusCode();
        }

        async Task<Guid> Queue(Guid tableSetId)
        {
            using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phone}/downloads", new { tableSetId, scope = "Full" }, Json);
            queued.EnsureSuccessStatusCode();
            return (await queued.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
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

    [Theory]
    [InlineData("link", "missing_link_ack", "Link acknowledgement was missing for MT131 fragment.")]
    [InlineData("table", "missing_table_ack", "Table acknowledgement was missing for MT131 (MT14).")]
    [InlineData("reject", "nak", "negative acknowledgement")]
    [InlineData("linkReject", "link_nak", "terminal rejected the install frame")]
    public async Task Interrupted_graphical_install_identifies_the_actual_failure_and_unsent_tables(
        string failure, string expectedStatus, string expectedReason)
    {
        var records = GraphicalStarterWire.Create();
        using var created = await fx.Client.PostAsJsonAsync("/api/tables/sets", new
        {
            name = $"Graphical failure diagnostics {failure}", isDefault = false
        }, Json);
        created.EnsureSuccessStatusCode();
        var setId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var definitions = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/definitions", Json))
            .EnumerateArray().ToDictionary(row => row.GetProperty("tableNumber").GetInt32(), row => row.GetProperty("id").GetGuid());
        foreach (var (number, payload) in records)
        {
            if (!definitions.TryGetValue(number, out var definition))
            {
                using var added = await fx.Client.PostAsJsonAsync("/api/tables/definitions", new { name = $"Graphical MT{number}", tableNumber = number }, Json);
                added.EnsureSuccessStatusCode();
                definition = (await added.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
            }
            using var version = await fx.Client.PostAsJsonAsync("/api/tables/versions", new
            {
                tableSetId = setId, tableDefinitionId = definition, tableRevision = 1,
                payloadBase64 = Convert.ToBase64String(payload), sortOrder = number
            }, Json);
            version.EnsureSuccessStatusCode();
        }
        using var published = await fx.Client.PostAsync($"/api/tables/sets/{setId}/publish?confirm=true", null);
        published.EnsureSuccessStatusCode();
        await AssignSetAsync(setId);
        try
        {
            var before = await BatchIdsAsync();
            await using (var link = await OpenAsync())
            {
                await link.Stream.WriteAsync(Message(KnownId, [44, 2]));
                AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                var header = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 18 }, header.Data);
                await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
                var first = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(131, first.Data[0]);
                if (failure == "linkReject")
                {
                    for (var retryIndex = 0; retryIndex < 3; retryIndex++)
                    {
                        await link.Stream.WriteAsync(Control((byte)(NccControl.Nack | first.Control)));
                        if (retryIndex < 2)
                        {
                            var retry = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                            Assert.Equal(first.Data, retry.Data);
                            Assert.Equal(first.Control | NccControl.ReTrans, retry.Control);
                        }
                    }
                }
                else if (failure != "link")
                {
                    var payload = records[131];
                    for (var offset = 0; offset < payload.Length; offset += NccConstants.MaxDataSize)
                    {
                        var part = offset == 0 ? first : Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                        Assert.Equal(payload[offset..Math.Min(offset + NccConstants.MaxDataSize, payload.Length)], part.Data);
                        await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | part.Control)));
                    }
                    if (failure == "reject")
                    {
                        await link.Stream.WriteAsync(Message(KnownId, [14, 0]));
                        AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                    }
                }
            }
            var batch = await WaitNewBatchAsync(before, 3);
            Assert.Contains(expectedReason, batch.GetProperty("lastError").GetString(), StringComparison.OrdinalIgnoreCase);
            var steps = batch.GetProperty("timeline").EnumerateArray().ToArray();
            Assert.Equal(3, steps.Length);
            Assert.Equal(expectedStatus, steps[0].GetProperty("lastAckStatus").GetString());
            Assert.Contains(expectedReason, steps[0].GetProperty("errorDetail").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.All(steps.Skip(1), step =>
            {
                Assert.Equal("not_sent", step.GetProperty("lastAckStatus").GetString());
                Assert.Equal("Not sent because the download stopped before this table.", step.GetProperty("errorDetail").GetString());
            });
        }
        finally
        {
            await AssignLabSetAsync();
        }
    }

    [Theory]
    [InlineData(false, "Start-of-download link acknowledgement was missing.", "not_sent")]
    [InlineData(true, "End-of-data link acknowledgement was missing.", "ack")]
    public async Task Missing_download_boundary_ack_preserves_table_delivery_evidence(
        bool acknowledgeTable, string reason, string expectedStatus)
    {
        var payload = new byte[1192];
        payload[0] = 73;
        await AssignRateTableSetAsync($"Missing boundary acknowledgement {acknowledgeTable}", payload);
        try
        {
            var before = await BatchIdsAsync();
            await using (var link = await OpenAsync())
            {
                await link.Stream.WriteAsync(Message(KnownId, [44, 2]));
                AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                var header = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 18 }, header.Data);
                if (acknowledgeTable)
                {
                    await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
                    for (var offset = 0; offset < payload.Length; offset += NccConstants.MaxDataSize)
                    {
                        var part = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                        Assert.Equal(payload[offset..Math.Min(offset + NccConstants.MaxDataSize, payload.Length)], part.Data);
                        await link.Stream.WriteAsync(Control((byte)(NccControl.Ack | part.Control)));
                    }
                    await link.Stream.WriteAsync(Message(KnownId, [14, 73]));
                    AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                    var end = Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
                    Assert.Equal(new byte[] { 13 }, end.Data);
                }
            }
            var batch = await WaitNewBatchAsync(before, 3);
            Assert.Equal(reason, batch.GetProperty("lastError").GetString());
            var step = batch.GetProperty("timeline")[0];
            Assert.Equal(expectedStatus, step.GetProperty("lastAckStatus").GetString());
            Assert.Equal(acknowledgeTable, step.GetProperty("succeeded").GetBoolean());
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
        Assert.Equal(0, created.GetProperty("status").GetInt32());
        using var cancel = await fx.Client.PostAsync($"/api/downloads/{created.GetProperty("id").GetGuid()}/cancel?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
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
        var replacement = await retry.Content.ReadFromJsonAsync<JsonElement>(Json);
        using var cancel = await fx.Client.PostAsync($"/api/downloads/{replacement.GetProperty("id").GetGuid()}/cancel?confirm=true", null);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
    }

    [Fact]
    public async Task Special_callback_retries_and_picks_up_assigned_tables_only_on_return()
    {
        byte[] wireId = Convert.FromHexString("0000000201");
        var terminals = await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json);
        var seed = terminals.EnumerateArray().Single(t => t.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId));
        using var created = await fx.Client.PostAsJsonAsync("/api/terminals", new
        {
            siteId = seed.GetProperty("siteId").GetGuid(), displayName = "Special callback test",
            terminalIdHex = Convert.ToHexString(wireId), status = 0
        }, Json);
        created.EnsureSuccessStatusCode();
        var terminalId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var endpoint = $"/api/terminals/{terminalId}/special-callback";
        var payload = new byte[1192];
        payload[^1] = 0x55;
        var tableSetId = await CreateRateTableSetAsync("Special callback test set", payload);
        using var assigned = await fx.Client.PostAsJsonAsync($"/api/terminals/{terminalId}/table-assignment?confirm=true",
            new { tableSetId }, Json);
        assigned.EnsureSuccessStatusCode();

        var due = fx.Factory.Clock.GetUtcNow().AddMinutes(2);
        const int terminalClockOffsetMinutes = -210;
        var expected = DlogCallbackWire.EncodeSchedule(due.UtcDateTime.AddMinutes(terminalClockOffsetMinutes));
        using var queued = await fx.Client.PutAsJsonAsync(endpoint, new
        {
            dueAtUtc = due, terminalClockOffsetMinutes, deliverAssignedTables = true
        }, Json);
        queued.EnsureSuccessStatusCode();
        var queuedRevision = (await ReadStatusAsync()).GetProperty("revision").GetInt32();
        var before = await BatchIdsAsync();

        foreach (var (request, response) in new[]
            { (DlogRateWire.EncodeRequest("5551212", false), (byte)64), (new byte[29], (byte)3) })
        {
            if (response == 3)
                request[0] = 1;
            await using var runtime = await OpenAsync();
            await runtime.Stream.WriteAsync(Message(wireId, request));
            AssertAck(await ReadFrameAsync(runtime.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(response, (await ReadAsync(runtime)).Data[0]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ReadFrameAsync(runtime.Stream, TimeSpan.FromMilliseconds(350)));
        }

        await using (var interrupted = await OpenAsync())
        {
            var maintenance = new byte[6 + 11 + 28 + 1];
            maintenance[0] = 6;
            maintenance[1] = 0x08;
            maintenance[6] = 10;
            maintenance[17] = 60;
            maintenance[^1] = 13;
            await interrupted.Stream.WriteAsync(Message(wireId, maintenance));
            AssertAck(await ReadFrameAsync(interrupted.Stream, TimeSpan.FromSeconds(5)));
            var first = await ReadAsync(interrupted);
            byte[] reply = [15, 0x08, 0x00, .. expected, 13];
            Assert.Equal(reply, first.Data);
            await interrupted.Stream.WriteAsync(Control((byte)(NccControl.Nack | first.Control)));
            var retry = await ReadAsync(interrupted);
            Assert.Equal(first.Control | NccControl.ReTrans, retry.Control);
            Assert.Equal(first.Data, retry.Data);
        }
        Assert.Equal(JsonValueKind.Null, (await ReadStatusAsync()).GetProperty("acknowledgedAtUtc").ValueKind);

        await using (var sync = await OpenAsync())
        {
            await sync.Stream.WriteAsync(Message(wireId, [DlogTimeSyncWire.Request]));
            AssertAck(await ReadFrameAsync(sync.Stream, TimeSpan.FromSeconds(5)));
            var clock = await ReadAsync(sync);
            Assert.Equal(DlogTimeSyncWire.Response, clock.Data[0]);
            await AckAsync(sync, clock);
            var offer = await ReadAsync(sync);
            Assert.Equal(expected, offer.Data);
            await AckAsync(sync, offer);
            var end = await ReadAsync(sync);
            Assert.Equal(new byte[] { 13 }, end.Data);
            await AckAsync(sync, end);
        }
        var programmed = await ReadStatusAsync();
        Assert.NotEqual(JsonValueKind.Null, programmed.GetProperty("acknowledgedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, programmed.GetProperty("returnedAtUtc").ValueKind);
        using var staleCancel = await fx.Client.PostAsJsonAsync($"{endpoint}/cancel", new { revision = queuedRevision }, Json);
        Assert.Equal(HttpStatusCode.Conflict, staleCancel.StatusCode);

        await using var callback = await OpenAsync();
        var earlyOffer = await OfferAsync(callback, DlogCallbackWire.Return);
        await AckAsync(callback, earlyOffer);
        Assert.Equal(new byte[] { 13 }, (await ReadAsync(callback)).Data);
        Assert.Equal(JsonValueKind.Null, (await ReadStatusAsync()).GetProperty("returnedAtUtc").ValueKind);
        Assert.True(before.SetEquals(await BatchIdsAsync()));

        fx.Factory.Clock.Advance(TimeSpan.FromMinutes(3));
        try
        {
            await callback.Stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket
            {
                Control = 1, TerminalId = wireId, Data = [DlogCallbackWire.Return]
            }));
            AssertAck(await ReadFrameAsync(callback.Stream, TimeSpan.FromSeconds(5)));
            var header = await ReadAsync(callback);
            Assert.Equal(new byte[] { 18 }, header.Data);
            await AckAsync(callback, header);
            for (var offset = 0; offset < payload.Length; offset += NccConstants.MaxDataSize)
            {
                var part = await ReadAsync(callback);
                Assert.Equal(payload[offset..Math.Min(offset + NccConstants.MaxDataSize, payload.Length)], part.Data);
                await AckAsync(callback, part);
            }
            await callback.Stream.WriteAsync(Message(wireId, [14, 73]));
            AssertAck(await ReadFrameAsync(callback.Stream, TimeSpan.FromSeconds(5)));
            var completed = await ReadAsync(callback);
            Assert.Equal(new byte[] { 13 }, completed.Data);
            await AckAsync(callback, completed);
            await WaitNewBatchAsync(before, 2);
            Assert.NotEqual(JsonValueKind.Null, (await ReadStatusAsync()).GetProperty("returnedAtUtc").ValueKind);
            await callback.Stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket
            {
                Control = 2, TerminalId = wireId, Data = [DlogCallbackWire.Return]
            }));
            AssertAck(await ReadFrameAsync(callback.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, (await ReadAsync(callback)).Data);
        }
        finally
        {
            fx.Factory.Clock.Advance(TimeSpan.FromMinutes(-3));
        }

        async Task<JsonElement> ReadStatusAsync() =>
            (await fx.Client.GetFromJsonAsync<JsonElement>(endpoint, Json)).GetProperty("callback");

        async Task<NccWirePacket> OfferAsync(Link link, byte type)
        {
            await link.Stream.WriteAsync(Message(wireId, [type]));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            var offer = await ReadAsync(link);
            Assert.Equal(expected, offer.Data);
            return offer;
        }

        static async Task<NccWirePacket> ReadAsync(Link link) =>
            Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));

        static ValueTask AckAsync(Link link, NccWirePacket frame) =>
            link.Stream.WriteAsync(Control((byte)(NccControl.Ack | frame.Control)));
    }

    [Fact]
    public async Task Terminal_identity_tracks_catalogue_changes_and_first_matched_contact_separately()
    {
        const string original = "0F0000A202";
        const string replacement = "0F0000A203";
        var seed = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json)).EnumerateArray()
            .Single(t => t.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId));
        var siteId = seed.GetProperty("siteId").GetGuid();
        using var created = await fx.Client.PostAsJsonAsync("/api/terminals", new
        {
            siteId, terminalIdHex = original, displayName = "Identity history fixture", terminalFamilyId = "mtr19-multipay", status = 0
        }, Json);
        created.EnsureSuccessStatusCode();
        var terminalId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var path = $"/api/terminals/{terminalId}";
        var identityPath = path + "/identity";

        var blank = await fx.Client.GetFromJsonAsync<JsonElement>(identityPath, Json);
        Assert.Equal(JsonValueKind.Null, blank.GetProperty("previousId").ValueKind);
        Assert.Equal(JsonValueKind.Null, blank.GetProperty("previousType").ValueKind);
        Assert.Equal(terminalId, (await Contact(original, 1)).GetProperty("terminalId").GetGuid());

        using var family = await fx.Client.PutAsJsonAsync(path + "/family", new { terminalFamilyId = "mtr20-desk" }, Json);
        family.EnsureSuccessStatusCode();
        using var duplicate = await ChangeId(original, Convert.ToHexString(KnownId));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var malformed = await ChangeId(original, "not-a-wire-id");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var rename = await ChangeId(original, replacement.ToLowerInvariant());
        rename.EnsureSuccessStatusCode();
        using var stale = await ChangeId(original, "0F0000A204");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var duplicateCreate = await fx.Client.PostAsJsonAsync("/api/terminals", new
        {
            siteId, terminalIdHex = replacement.ToLowerInvariant(), displayName = "Conflicting identity fixture", status = 0
        }, Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicateCreate.StatusCode);

        var pending = await fx.Client.GetFromJsonAsync<JsonElement>(identityPath, Json);
        Assert.Equal(replacement, pending.GetProperty("terminalIdHex").GetString());
        Assert.Equal(original, pending.GetProperty("previousId").GetProperty("value").GetString());
        Assert.Equal("MTR 1.9 Multi-pay", pending.GetProperty("previousType").GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("firstContactAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await Contact(original, 2)).GetProperty("terminalId").ValueKind);
        Assert.Equal(terminalId, (await Contact(replacement, 3)).GetProperty("terminalId").GetGuid());

        var observed = await fx.Client.GetFromJsonAsync<JsonElement>(identityPath, Json);
        var first = observed.GetProperty("firstContactAtUtc").GetDateTime();
        Assert.True(first >= observed.GetProperty("previousId").GetProperty("changedAtUtc").GetDateTime());
        await Contact(replacement, 4);
        var again = await fx.Client.GetFromJsonAsync<JsonElement>(identityPath, Json);
        Assert.Equal(first, again.GetProperty("firstContactAtUtc").GetDateTime());

        using var clear = await fx.Client.PutAsJsonAsync(path + "/family", new { terminalFamilyId = (string?)null }, Json);
        clear.EnsureSuccessStatusCode();
        using var legacy = await fx.Client.PutAsJsonAsync(path, new
        {
            terminalIdHex = original, displayName = "Identity history fixture", status = 0
        }, Json);
        legacy.EnsureSuccessStatusCode();
        var returned = await fx.Client.GetFromJsonAsync<JsonElement>(identityPath, Json);
        Assert.Equal(replacement, returned.GetProperty("previousId").GetProperty("value").GetString());
        Assert.Equal("MTR 2.0 Desk", returned.GetProperty("previousType").GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, returned.GetProperty("firstContactAtUtc").ValueKind);

        Task<HttpResponseMessage> ChangeId(string expected, string desired) =>
            fx.Client.PutAsJsonAsync(identityPath, new { expectedTerminalIdHex = expected, terminalIdHex = desired }, Json);

        async Task<JsonElement> Contact(string wireId, byte marker)
        {
            var packet = Message(Convert.FromHexString(wireId), [84, marker]);
            await using (var link = await OpenAsync())
            {
                await link.Stream.WriteAsync(packet);
                AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            }
            return await WaitSessionAsync(Convert.ToHexString(packet));
        }
    }

    [Fact]
    public async Task Terminal_inventory_keeps_operator_details_separate_from_reported_serial()
    {
        const string wireId = "0000000202";
        var terminals = await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json);
        var seed = terminals.EnumerateArray().Single(t => t.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId));
        using var create = await fx.Client.PostAsJsonAsync("/api/terminals", new
        {
            siteId = seed.GetProperty("siteId").GetGuid(), displayName = "Inventory test",
            terminalIdHex = wireId, status = 0
        }, Json);
        create.EnsureSuccessStatusCode();
        var terminalId = (await create.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var endpoint = $"/api/terminals/{terminalId}/inventory";
        var inventory = await fx.Client.GetFromJsonAsync<JsonElement>(endpoint, Json);
        Assert.Equal(JsonValueKind.Null, inventory.GetProperty("revision").ValueKind);
        Assert.Equal(JsonValueKind.Null, inventory.GetProperty("serialNumber").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(inventory.GetProperty("location").GetString()));

        using var invalid = await fx.Client.PutAsJsonAsync(endpoint, new { profile = new { billingCustomerCode = "AB" } }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var fractional = await fx.Client.PutAsJsonAsync(endpoint, new { profile = new { equipmentCost = 12.345m } }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, fractional.StatusCode);
        using var save = await fx.Client.PutAsJsonAsync(endpoint, new
        {
            profile = new { regionCode = " LAB-INV ", route = "North", addressLine2 = "Service area", indoor = true,
                salespersonCode = "0000042", billingCustomerCode = "007", equipmentCost = 12.35m, signCount = 3 }
        }, Json);
        save.EnsureSuccessStatusCode();
        var saved = await save.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(1, saved.GetProperty("revision").GetInt32());
        Assert.Equal("LAB-INV", saved.GetProperty("profile").GetProperty("regionCode").GetString());

        var bundle = new byte[2 + 11 + 28];
        bundle[0] = 44;
        bundle[1] = 2;
        bundle[2] = 10;
        Convert.FromHexString("0012345678").CopyTo(bundle, 3);
        bundle[13] = 60;
        await using (var link = await OpenAsync())
        {
            await link.Stream.WriteAsync(Message(Convert.FromHexString(wireId), bundle));
            AssertAck(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5))).Data);
        }
        inventory = await fx.Client.GetFromJsonAsync<JsonElement>(endpoint, Json);
        Assert.Equal("0012345678", inventory.GetProperty("serialNumber").GetString());
        Assert.NotEqual(JsonValueKind.Null, inventory.GetProperty("serialReportedAtUtc").ValueKind);
        Assert.Equal("0000042", inventory.GetProperty("profile").GetProperty("salespersonCode").GetString());
        Assert.Equal(12.35m, inventory.GetProperty("profile").GetProperty("equipmentCost").GetDecimal());
        Assert.True(inventory.GetProperty("profile").GetProperty("indoor").GetBoolean());

        using var update = await fx.Client.PutAsJsonAsync(endpoint, new
        {
            revision = 1, profile = new { regionCode = "Lab-New", billingCustomerCode = "007", signCount = 0 }
        }, Json);
        update.EnsureSuccessStatusCode();
        using var stale = await fx.Client.PutAsJsonAsync(endpoint, new { revision = 1, profile = new { } }, Json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var matches = await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals?regionCode=lab-new&page=1", Json);
        Assert.Equal(1, matches.GetProperty("total").GetInt32());
        Assert.Equal(wireId, matches.GetProperty("items")[0].GetProperty("terminalIdHex").GetString());
        Assert.Equal("Lab-New", matches.GetProperty("items")[0].GetProperty("regionCode").GetString());
    }

    [Fact]
    public async Task Scheduled_region_group_records_send_only_the_selection_and_dependencies_on_call_in()
    {
        var seed = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json)).EnumerateArray()
            .Single(t => t.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId));
        var siteId = seed.GetProperty("siteId").GetGuid();
        using var groupResponse = await fx.Client.PostAsJsonAsync("/api/terminal-groups", new { name = "Regional transfer group" }, Json);
        groupResponse.EnsureSuccessStatusCode();
        var group = (await groupResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        const string wire = "0000000451";
        var phone = await CreatePhoneAsync(wire, group, "Lab-East");
        var otherRegion = await CreatePhoneAsync("0000000452", group, "Lab-West");
        var sameRegion = await CreatePhoneAsync("0000000453", null, "LAB-EAST");
        var regions = await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals/regions", Json);
        Assert.Equal(2, regions.EnumerateArray().Single(row => row.GetProperty("id").GetString() == "lab-east")
            .GetProperty("phoneCount").GetInt32());

        var basisPayload = new byte[1192];
        basisPayload[^1] = 0x22;
        var basis = await CreateRateTableSetAsync("Regional schedule retained base", basisPayload);
        using var assigned = await fx.Client.PostAsJsonAsync($"/api/terminals/{phone}/table-assignment",
            new { tableSetId = basis }, Json);
        assigned.EnsureSuccessStatusCode();

        using var create = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = "Regional partial schedule source" }, Json);
        create.EnsureSuccessStatusCode();
        var setId = (await create.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var definitions = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/definitions", Json)).EnumerateArray()
            .Where(row => row.GetProperty("tableNumber").GetInt32() is 21 or 26 or 73)
            .ToDictionary(row => row.GetProperty("tableNumber").GetInt32(), row => row.GetProperty("id").GetGuid());
        byte[] features = new byte[72];
        features[0] = 26;
        byte[] rates = new byte[1192];
        rates[0] = 73;
        rates[^1] = 0x33;
        byte[] access = new byte[18];
        access[0] = 21;
        Convert.FromHexString("0000000499").CopyTo(access, 1);
        await AddRecordAsync(access, 0);
        await AddRecordAsync(features, 1);
        await AddRecordAsync(rates, 2, definitions[26]);
        var endpoint = $"/api/tables/sets/{setId}";
        using var publish = await fx.Client.PostAsync($"{endpoint}/publish?confirm=true", null);
        publish.EnsureSuccessStatusCode();

        var preview = await fx.Client.GetFromJsonAsync<JsonElement>($"{endpoint}/assignment-preview?targetType=region&regionCode=lab-east", Json);
        Assert.Equal(new[] { phone, sameRegion }.Order(), preview.GetProperty("targets").EnumerateArray()
            .Select(row => row.GetProperty("id").GetGuid()).Order());
        Assert.All(preview.GetProperty("targets").EnumerateArray(), row => Assert.False(row.GetProperty("eligible").GetBoolean()));
        var path = $"{endpoint}/assignment-preview?targetType=group&targetId={group}&regionCode=LAB-East&recordNumbers=73";
        preview = await fx.Client.GetFromJsonAsync<JsonElement>(path, Json);
        var target = Assert.Single(preview.GetProperty("targets").EnumerateArray());
        Assert.Equal(phone, target.GetProperty("id").GetGuid());
        Assert.True(target.GetProperty("eligible").GetBoolean());
        Assert.Equal(new[] { 26, 73 }, preview.GetProperty("recordNumbers").Deserialize<int[]>());

        var schedule = $"{endpoint}/schedules";
        using var forged = await fx.Client.PostAsJsonAsync(schedule, Request([phone, otherRegion], [73]), Json);
        Assert.Equal(HttpStatusCode.Conflict, forged.StatusCode);
        using var queued = await fx.Client.PostAsJsonAsync(schedule, Request([phone], [73]), Json);
        queued.EnsureSuccessStatusCode();
        using var replay = await fx.Client.PostAsJsonAsync(schedule, Request([phone], [26, 73]), Json);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(1, (await replay.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("alreadyScheduled").GetInt32());
        using var different = await fx.Client.PostAsJsonAsync(schedule, Request([phone], [26]), Json);
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);

        var prepared = (await fx.Client.GetFromJsonAsync<JsonElement>(schedule, Json))[0];
        var preparedBatchId = prepared.GetProperty("lastBatchId").GetGuid();
        Assert.Equal("Ready", prepared.GetProperty("status").GetString());
        var pending = await WaitBatchAsync(preparedBatchId, 0);
        Assert.Equal(2, pending.GetProperty("timeline").GetArrayLength());
        var before = await BatchIdsAsync();
        await using (var install = await OpenAsync())
        {
            await install.Stream.WriteAsync(Message(Convert.FromHexString(wire), [44, 2]));
            AssertAck(await ReadFrameAsync(install.Stream, TimeSpan.FromSeconds(5)));
            var header = await ReadAsync(install);
            Assert.Equal(new byte[] { 18 }, header.Data);
            await AckAsync(install, header);
            Assert.Equal(basisPayload[..NccConstants.MaxDataSize], (await ReadAsync(install)).Data);
        }
        await WaitNewBatchAsync(before, 3);
        await using (var callIn = await OpenAsync())
        {
            await callIn.Stream.WriteAsync(Message(Convert.FromHexString(wire), [8]));
            AssertAck(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            var header = await ReadAsync(callIn);
            Assert.Equal(new byte[] { 18 }, header.Data);
            await AckAsync(callIn, header);
            foreach (var payload in new[] { features, rates })
            {
                for (var offset = 0; offset < payload.Length; offset += NccConstants.MaxDataSize)
                {
                    var fragment = await ReadAsync(callIn);
                    Assert.Equal(payload[offset..Math.Min(offset + NccConstants.MaxDataSize, payload.Length)], fragment.Data);
                    await AckAsync(callIn, fragment);
                }
                await callIn.Stream.WriteAsync(Message(Convert.FromHexString(wire), [14, payload[0]]));
                AssertAck(await ReadFrameAsync(callIn.Stream, TimeSpan.FromSeconds(5)));
            }
            var end = await ReadAsync(callIn);
            Assert.Equal(new byte[] { 13 }, end.Data);
            await callIn.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
            await WaitBatchAsync(preparedBatchId, 2);
        }

        var delivered = (await fx.Client.GetFromJsonAsync<JsonElement>(schedule, Json))[0];
        Assert.Equal("Delivered", delivered.GetProperty("status").GetString());
        Assert.Equal(preparedBatchId, delivered.GetProperty("lastBatchId").GetGuid());
        Assert.Equal(new[] { 26, 73 }, delivered.GetProperty("recordNumbers").Deserialize<int[]>());
        var assignment = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/terminals/{phone}/table-assignment", Json);
        Assert.Equal(basis, assignment.GetProperty("tableSetId").GetGuid());

        async Task<Guid> CreatePhoneAsync(string terminalIdHex, Guid? terminalGroupId, string regionCode)
        {
            using var created = await fx.Client.PostAsJsonAsync("/api/terminals", new
            {
                displayName = $"Regional fixture {terminalIdHex[^2..]}", siteId, terminalIdHex, terminalGroupId, status = 0
            }, Json);
            created.EnsureSuccessStatusCode();
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
            using var saved = await fx.Client.PutAsJsonAsync($"/api/terminals/{id}/inventory", new { profile = new { regionCode } }, Json);
            saved.EnsureSuccessStatusCode();
            return id;
        }

        async Task AddRecordAsync(byte[] payload, int sortOrder, Guid? dependsOnTableDefinitionId = null)
        {
            using var version = await fx.Client.PostAsJsonAsync("/api/tables/versions", new
            {
                tableSetId = setId, tableDefinitionId = definitions[payload[0]], tableRevision = 1, sortOrder,
                payloadBase64 = Convert.ToBase64String(payload), dependsOnTableDefinitionId
            }, Json);
            version.EnsureSuccessStatusCode();
        }

        object Request(Guid[] terminalIds, int[] recordNumbers) =>
            new { targetType = "group", targetId = group, regionCode = "Lab-East", terminalIds, recordNumbers };

        static async Task<NccWirePacket> ReadAsync(Link link) =>
            Decode(await ReadFrameAsync(link.Stream, TimeSpan.FromSeconds(5)));

        static ValueTask AckAsync(Link link, NccWirePacket frame) =>
            link.Stream.WriteAsync(Control((byte)(NccControl.Ack | frame.Control)));
    }

    private async Task AssignLabSetAsync()
    {
        var sets = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/sets", Json))!;
        var lab = sets.EnumerateArray().First(s => s.GetProperty("name").GetString() == "Lab install set");
        await AssignSetAsync(lab.GetProperty("id").GetGuid());
    }

    private async Task AssignRateTableSetAsync(string name, byte[]? payload = null)
        => await AssignSetAsync(await CreateRateTableSetAsync(name, payload));

    [Theory]
    [InlineData(8, 26)]
    [InlineData(44, 26)]
    [InlineData(44, 58)]
    [InlineData(44, 68)]
    [InlineData(44, 32)]
    [InlineData(44, 34)]
    public async Task Terminal_call_sends_only_the_selected_records_and_acknowledges_the_original_batch(int trigger, int selectedType)
    {
        var terminal = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/terminals", Json)).EnumerateArray()
            .Single(row => row.GetProperty("terminalIdHex").GetString() == Convert.ToHexString(KnownId));
        var phoneId = terminal.GetProperty("id").GetGuid();
        var definitions = (await fx.Client.GetFromJsonAsync<JsonElement>("/api/tables/definitions", Json)).EnumerateArray()
            .Where(row => row.GetProperty("tableNumber").GetInt32() == selectedType || row.GetProperty("tableNumber").GetInt32() == 73)
            .ToDictionary(row => row.GetProperty("tableNumber").GetInt32(), row => row.GetProperty("id").GetGuid());
        if (!definitions.ContainsKey(selectedType))
        {
            using var definition = await fx.Client.PostAsJsonAsync("/api/tables/definitions", new { name = $"Selected record fixture MT{selectedType}", tableNumber = selectedType }, Json);
            definition.EnsureSuccessStatusCode();
            definitions.Add(selectedType, (await definition.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid());
        }
        var assignedSet = await CreateRateTableSetAsync("Queued download preserved assignment");
        await AssignSetAsync(assignedSet);

        using var source = await fx.Client.PostAsJsonAsync("/api/tables/sets", new { name = $"Selected download MT{selectedType} on MT{trigger} fixture" }, Json);
        source.EnsureSuccessStatusCode();
        var tableSetId = (await source.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var selected = new byte[selectedType switch { 32 => 33, 34 => 19, 58 => 26, _ => 72 }];
        selected[0] = (byte)selectedType;
        selected[^1] = 0x43;
        if (selectedType == 58)
            selected = NativeTableLayouts.Find(58, selected)!.Apply(selected, new Dictionary<string, string>
            {
                ["restriction"] = "2", ["startHour"] = "20", ["durationHours"] = "6"
            });
        else if (selectedType == 68)
            selected = NativeTableLayouts.Find(68, selected)!.Apply(selected, new Dictionary<string, string>
            {
                ["statistics"] = "1", ["lineReleaseDelay"] = "300", ["spare4"] = "100"
            });
        else if (selectedType == 32)
        {
            selected[9] = 0x43;
            selected[^1] = 0;
            selected = NativeTableLayouts.Find(32, selected)!.Apply(selected, new Dictionary<string, string>
            {
                ["days"] = "1", ["threshold"] = "2", ["interval-0.start"] = "10", ["interval-0.duration"] = "20"
            });
        }
        else if (selectedType == 34)
        {
            selected[13] = 0xC0;
            selected = NativeTableLayouts.Find(34, selected)!.Apply(selected, new Dictionary<string, string>
            {
                ["days"] = "1", ["threshold"] = "2", ["carrierStatistics"] = "1", ["enabled"] = "4",
                ["detailThreshold"] = "40", ["detailHour"] = "20", ["detailHours"] = "6"
            });
        }
        var unrelated = new byte[1192];
        unrelated[0] = 73;
        foreach (var payload in new[] { selected, unrelated })
        {
            using var version = await fx.Client.PostAsJsonAsync("/api/tables/versions", new
            {
                tableSetId, tableDefinitionId = definitions[payload[0]], tableRevision = 1,
                payloadBase64 = Convert.ToBase64String(payload), sortOrder = (int)payload[0]
            }, Json);
            version.EnsureSuccessStatusCode();
        }
        using var published = await fx.Client.PostAsync($"/api/tables/sets/{tableSetId}/publish?confirm=true", null);
        published.EnsureSuccessStatusCode();
        using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phoneId}/downloads", new
        {
            tableSetId, scope = "Partial", partialTableDefinitionIds = new[] { definitions[selectedType] }
        }, Json);
        queued.EnsureSuccessStatusCode();
        var prepared = await queued.Content.ReadFromJsonAsync<JsonElement>(Json);
        var batchId = prepared.GetProperty("id").GetGuid();
        Assert.Equal(0, prepared.GetProperty("status").GetInt32());
        var batchPath = $"/api/downloads/{batchId}";
        var prior = await BatchIdsAsync();

        try
        {
            await using var call = await OpenAsync();
            var request = trigger == 8 ? new byte[] { 8 } : new byte[] { 44, 1 };
            await call.Stream.WriteAsync(Message(KnownId, request));
            AssertAck(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
            var header = Decode(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 18 }, header.Data);
            var active = await fx.Client.GetFromJsonAsync<JsonElement>(batchPath, Json);
            Assert.Equal(1, active.GetProperty("status").GetInt32());
            var sessionId = active.GetProperty("nccSessionId").GetGuid();
            using var cancel = await fx.Client.PostAsync($"{batchPath}/cancel?confirm=true", null);
            Assert.Equal(HttpStatusCode.BadRequest, cancel.StatusCode);

            await using (var competing = await OpenAsync())
            {
                await competing.Stream.WriteAsync(Message(KnownId, [44, 2]));
                AssertAck(await ReadFrameAsync(competing.Stream, TimeSpan.FromSeconds(5)));
                Assert.Equal(new byte[] { 13 }, Decode(await ReadFrameAsync(competing.Stream, TimeSpan.FromSeconds(5))).Data);
            }

            await call.Stream.WriteAsync(Control((byte)(NccControl.Ack | header.Control)));
            var record = Decode(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(selected, record.Data);
            await call.Stream.WriteAsync(Control((byte)(NccControl.Ack | record.Control)));
            await call.Stream.WriteAsync(Message(KnownId, [14, (byte)selectedType]));
            AssertAck(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
            var end = Decode(await ReadFrameAsync(call.Stream, TimeSpan.FromSeconds(5)));
            Assert.Equal(new byte[] { 13 }, end.Data);
            await call.Stream.WriteAsync(Control((byte)(NccControl.Ack | NccControl.Clr | end.Control)));

            JsonElement completed = default;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                completed = await fx.Client.GetFromJsonAsync<JsonElement>(batchPath, Json);
                if (completed.GetProperty("status").GetInt32() == 2)
                    break;
                await Task.Delay(100);
            }
            Assert.Equal(2, completed.GetProperty("status").GetInt32());
            Assert.Equal(sessionId, completed.GetProperty("nccSessionId").GetGuid());
            var step = Assert.Single(completed.GetProperty("timeline").EnumerateArray());
            Assert.Equal("ack", step.GetProperty("lastAckStatus").GetString());
            Assert.True(prior.SetEquals(await BatchIdsAsync()));
            var assignment = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/terminals/{phoneId}/table-assignment", Json);
            Assert.Equal(assignedSet, assignment.GetProperty("tableSetId").GetGuid());
        }
        finally
        {
            await AssignLabSetAsync();
        }
    }

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

    private async Task<JsonElement> WaitBatchAsync(Guid id, int status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var batch = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/downloads/{id}", Json);
            if (batch.GetProperty("status").GetInt32() == status)
                return batch;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Download batch {id} did not reach status {status}.");
    }

    private async Task<JsonElement> WaitSessionAsync(string frameHex, Guid? sessionId = null)
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
                    && (sessionId == null || sessionId == row.GetProperty("id").GetGuid())
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
        byte[] memoryLost = [7, 126, 11, 23, 12, 0, 0, 22];
        memoryLost.CopyTo(bundle, 0);
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
        var terminalReports = rows.Where(row => row.GetProperty("messageType").GetInt32() == 7).ToList();
        foreach (var (number, summary, phoneTime) in new (int, string, string?)[]
        {
            (22, "Memory lost (alarm 22)", "2026-11-23T12:00:00"),
            (91, "Unlisted alarm 91", null)
        })
        {
            var row = terminalReports.First(eventRow => eventRow.GetProperty("summary").GetString() == summary);
            var detail = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions/{row.GetProperty("id").GetGuid()}", Json);
            var fieldEvent = detail.GetProperty("fieldEvent");
            Assert.Equal("alarm", fieldEvent.GetProperty("category").GetString());
            Assert.Equal(summary, fieldEvent.GetProperty("summary").GetString());
            Assert.Equal(number, fieldEvent.GetProperty("alarmNumber").GetInt32());
            Assert.Equal(phoneTime, fieldEvent.GetProperty("phoneTime").GetString());
        }
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
