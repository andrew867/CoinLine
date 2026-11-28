using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresShadyBankAuthorizationTests
{
    [Theory]
    [InlineData("both")]
    [InlineData("remote-http")]
    [InlineData("missing-key")]
    [InlineData("bad-instrument")]
    [InlineData("query")]
    [InlineData("capture-without-binding")]
    [InlineData("capture-without-offset")]
    public async Task Invalid_provider_configuration_refuses_startup(string mode)
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Testing").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("CardPayments:LabAuthorization:Enabled", mode == "both" ? "true" : "false")
            .UseSetting("CardPayments:ShadyBankAuthorization:Enabled", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:AllowLoopbackHttp", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:ServiceUrl", mode == "remote-http" ? "http://bank.example.invalid/api/merchant/authorize" :
                "https://bank.example.invalid/api/merchant/authorize" + (mode == "query" ? "?unexpected=1" : ""))
            .UseSetting("CardPayments:ShadyBankAuthorization:ServiceKey", mode == "missing-key" ? "" : new string('K',43))
            .UseSetting("CardPayments:ShadyBankAuthorization:CaptureUploadedCalls", mode.StartsWith("capture-", StringComparison.Ordinal) ? "true" : "false")
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:TerminalId", Guid.NewGuid().ToString())
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:Destination", "3335550123")
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:AllowCaptureUploadedCalls", mode == "capture-without-offset" ? "true" : "false")
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:InstrumentToken", mode == "bad-instrument" ? "bad" : new string('T',43)));
        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());
        Assert.Contains(mode == "both" ? "Choose either" : mode.StartsWith("capture-", StringComparison.Ordinal) ? "ShadyBank capture requires" : "ShadyBank requires", error.Message);
    }

    [PostgresIntegrationFact]
    public async Task Bank_provider_only_sends_an_opaque_token_and_quote_and_replays_without_settlement()
    {
        const string route = "3335550123";
        var key = Guid.NewGuid().ToString("N");
        var serviceKey = new string('K', 43); // transport fixture, not a bank credential
        var instrument = new string('T', 43);
        var bank = new BankFixture(serviceKey, instrument);
        var customer = new Customer { Name = "Merchant fixture", Code = Guid.NewGuid().ToString("N")[..12] };
        var site = new Site { CustomerId = customer.Id, Name = "Isolated test site", Code = "LAB" };
        var wireId = Guid.NewGuid().ToByteArray()[..5];
        var otherId = Guid.NewGuid().ToByteArray()[..5];
        var terminal = new Terminal { SiteId = site.Id, TerminalIdHex = Convert.ToHexString(wireId), DisplayName = "Synthetic merchant phone" };
        var other = new Terminal { SiteId = site.Id, TerminalIdHex = Convert.ToHexString(otherId), DisplayName = "Unbound fixture phone" };
        var plan = new RatePlan { CustomerId = customer.Id, Name = "Synthetic merchant plan", Mode = RatingMode.RealTimeRated };
        var version = new RatePlanVersion { RatePlanId = plan.Id, VersionNumber = 1, Status = RatePlanVersionStatus.Published, PublishedAtUtc = DateTime.UtcNow };
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint!).Port; probe.Stop();
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Ncc:Port", port.ToString())
            .UseSetting("CardPayments:AuthorizationTimeoutMilliseconds", "1500")
            .UseSetting("CardPayments:ShadyBankAuthorization:Enabled", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:ServiceUrl", "http://127.0.0.1:8022/api/merchant/authorize")
            .UseSetting("CardPayments:ShadyBankAuthorization:AllowLoopbackHttp", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:ServiceKey", serviceKey)
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:TerminalId", terminal.Id.ToString())
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:Destination", route)
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:InstrumentToken", instrument)
            .ConfigureServices(services => services.AddHttpClient("shadybank-authorization").ConfigurePrimaryHttpMessageHandler(() => bank)));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(host, "bank-fixture", "Admin");
        admin.DefaultRequestHeaders.Add("X-API-Key", key);
        int beforePayments;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Contains("Npgsql", db.Database.ProviderName!);
            db.AddRange(customer, site, terminal, other, plan, version);
            db.RateRules.Add(new RateRule { RatePlanVersionId = version.Id, Priority = 1, MatchKind = RateRuleMatchKind.Exact,
                Pattern = route, Outcome = RateRuleOutcome.Rated, RatePerMinuteUsd = 0.05m });
            await db.SaveChangesAsync(); plan.PublishedVersionId = version.Id; await db.SaveChangesAsync();
            beforePayments = await db.PaymentTransactions.CountAsync();
        }
        var state = await admin.GetFromJsonAsync<JsonElement>("/api/cards/simulation-state");
        Assert.True(state.GetProperty("shadyBankAuthorizationEnabled").GetBoolean());
        Assert.False(state.GetProperty("labAuthorizationEnabled").GetBoolean());
        var request = ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 1);
        Assert.Equal(1, (await Exchange(wireId, request))[1]); // no quote
        Assert.Equal(1, (await Exchange(otherId, request))[1]); // no binding
        var unknown = request.ToArray(); unknown[13] ^= 1;
        Assert.Equal(1, (await Exchange(wireId, unknown))[1]);
        Assert.Empty(bank.RequestIds);

        await Quote();
        var approved = await Exchange(wireId, request, retransmit: true);
        Assert.Equal((byte)0, approved[1]);
        Assert.Equal("SB123456"u8.ToArray(), approved[2..10]);
        Assert.Single(bank.RequestIds);
        Assert.Equal(approved, await Exchange(wireId, request)); // full reconnect uses same durable request ID
        Assert.Equal(2, bank.RequestIds.Count); Assert.Single(bank.RequestIds.Distinct());
        Assert.Equal(1, (await Exchange(wireId, ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 2)))[1]);
        Assert.Equal(2, bank.RequestIds.Count); // another call cannot reuse a claimed quote

        ushort sequence = 3;
        foreach (var mode in new[] { "decline", "http", "invalid", "timeout" })
        {
            bank.Mode = mode; await Quote();
            Assert.Equal(1, (await Exchange(wireId, ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, sequence++)))[1]);
        }
        bank.Mode = "approve"; await Quote();
        Assert.Equal(new byte[] { 3, 0 }, await Exchange(wireId, ManualCardLabTests.SyntheticAuthorizationRequest(route, 1, sequence)));
        Assert.Equal(2, bank.Reservations.Count);

        var history = await admin.GetFromJsonAsync<JsonElement>("/api/cards/authorization-attempts?terminalId=" + terminal.Id);
        var attempts = history.EnumerateArray().ToArray();
        Assert.All(attempts, row => { Assert.Equal("shadybank", row.GetProperty("provider").GetString()); Assert.Equal(JsonValueKind.Null, row.GetProperty("cardAccountId").ValueKind); });
        foreach (var outcome in new[] { "approved", "declined", "provider_http_error", "provider_invalid_decision", "provider_timeout" })
            Assert.Contains(attempts, row => row.GetProperty("outcome").GetString() == outcome);
        Assert.Contains(attempts, row => row.GetProperty("reason").GetString() == "quote_already_used");
        Assert.Contains(attempts, row => row.GetProperty("reason").GetString() == "bank_declined");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(beforePayments, await db.PaymentTransactions.CountAsync());
            var rows = await db.AuditEvents.Where(row => row.TerminalId == terminal.Id).ToListAsync();
            Assert.Equal(6, rows.Count(row => row.Action == "merchant_authorization_claim"));
            Assert.All(rows, row => { Assert.DoesNotContain(serviceKey, row.DetailJson); Assert.DoesNotContain(instrument, row.DetailJson); Assert.DoesNotContain("SB123456", row.DetailJson); });
            var sessionIds = rows.Where(row => row.Action == "card_authorization_decision").Select(row => Guid.Parse(row.TerminalSessionId!)).ToArray();
            var protectedFrames = await db.NccSessionFrames.Where(row => sessionIds.Contains(row.NccSessionId) && (row.SentByHost || row.WireLength == 40 || row.WireLength == 50)).ToListAsync();
            Assert.NotEmpty(protectedFrames); Assert.All(protectedFrames, row => Assert.Empty(row.RawBytes));
        }

        async Task Quote()
        {
            var reply = await Exchange(wireId, DlogRateWire.EncodeRequest(route, false));
            Assert.Equal(64, reply[0]); Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(4, 2)));
        }

        async Task<byte[]> Exchange(byte[] id, byte[] payload, bool retransmit = false)
        {
            using var tcp = new TcpClient(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await tcp.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            var stream = tcp.GetStream();
            byte[] Encode(byte control, byte[] data) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = id, Data = data });
            await stream.WriteAsync(Encode(0, payload), deadline.Token);
            Assert.NotEqual(0, (await ReadFrame(stream, deadline.Token)).Control & NccControl.Ack);
            var response = await ReadFrame(stream, deadline.Token);
            if (retransmit)
            {
                await stream.WriteAsync(Encode(NccControl.ReTrans, payload), deadline.Token);
                Assert.NotEqual(0, (await ReadFrame(stream, deadline.Token)).Control & NccControl.Ack);
                Assert.Equal(response.Data, (await ReadFrame(stream, deadline.Token)).Data);
            }
            await stream.WriteAsync(Encode((byte)(NccControl.Ack | (response.Control & 3)), []), deadline.Token);
            await stream.WriteAsync(Encode(NccControl.Clr, []), deadline.Token);
            return response.Data;
        }
    }

    [PostgresIntegrationFact]
    public async Task Charged_call_upload_posts_one_scoped_bank_receipt_after_retries()
    {
        const string route = "3335550123";
        var key = Guid.NewGuid().ToString("N");
        var serviceKey = new string('K', 43);
        var instrument = new string('T', 43);
        var bank = new BankFixture(serviceKey, instrument) { LoseFirstCaptureReply = true };
        var customer = new Customer { Name = "Capture fixture", Code = Guid.NewGuid().ToString("N")[..12] };
        var site = new Site { CustomerId = customer.Id, Name = "Isolated site", Code = "LAB" };
        var wireId = Guid.NewGuid().ToByteArray()[..5];
        var terminal = new Terminal { SiteId = site.Id, TerminalIdHex = Convert.ToHexString(wireId), DisplayName = "Synthetic capture phone" };
        var plan = new RatePlan { CustomerId = customer.Id, Name = "Capture fixture rate", Mode = RatingMode.RealTimeRated };
        var version = new RatePlanVersion { RatePlanId = plan.Id, VersionNumber = 1, Status = RatePlanVersionStatus.Published, PublishedAtUtc = DateTime.UtcNow };
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint!).Port; probe.Stop();
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Ncc:Port", port.ToString())
            .UseSetting("CardPayments:ShadyBankAuthorization:Enabled", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:ServiceUrl", "http://127.0.0.1:8022/api/merchant/authorize")
            .UseSetting("CardPayments:ShadyBankAuthorization:AllowLoopbackHttp", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:ServiceKey", serviceKey)
            .UseSetting("CardPayments:ShadyBankAuthorization:CaptureUploadedCalls", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:TerminalId", terminal.Id.ToString())
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:Destination", route)
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:InstrumentToken", instrument)
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:AllowCaptureUploadedCalls", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:AllowAuthorizationOnlyRate", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:TerminalClockOffsetMinutes", "0")
            .ConfigureServices(services => services.AddHttpClient("shadybank-authorization").ConfigurePrimaryHttpMessageHandler(() => bank)));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(host);
        admin.DefaultRequestHeaders.Add("X-API-Key", key);
        int beforePayments;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(customer, site, terminal, plan, version);
            db.RateRules.Add(new RateRule { RatePlanVersionId = version.Id, Priority = 1, MatchKind = RateRuleMatchKind.Exact,
                Pattern = route, Outcome = RateRuleOutcome.Rated, RatePerMinuteUsd = 0.05m });
            await db.SaveChangesAsync(); plan.PublishedVersionId = version.Id; await db.SaveChangesAsync();
            beforePayments = await db.PaymentTransactions.CountAsync();
        }
        var started = DateTime.UtcNow;
        // Exercise both the legacy authorization-only path and the normal quoted path.
        Assert.Equal(0, (await Exchange(ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 50)))[1]);
        Assert.Equal(0, (await Exchange(ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 51)))[1]);
        Assert.Equal(64, (await Exchange(DlogRateWire.EncodeRequest(route, false)))[0]);
        Assert.Equal(0, (await Exchange(ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 52)))[1]);
        Assert.Equal(3, bank.Reservations.Count);
        Assert.Empty(bank.Captures);
        await Upload(Cdr(50, started, 0, 10, 0x44)); // genuine-shaped zero charge is not a debit
        await Upload(Cdr(52, started, 6, 10, 0x44)); // amount differs from the quote
        await Upload(Cdr(52, started, 5, 61, 0x44)); // beyond authorized period
        await Upload(Cdr(52, started, 5, 10, 0x41)); // unanswered
        await Upload(Cdr(99, started, 5, 10, 0x44)); // no matching reservation
        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.Empty(bank.Captures);
        Assert.Empty((await List()).EnumerateArray());
        var valid = Cdr(52, started, 5, 10, 0x44);
        await Upload(valid); await Upload(valid);
        await Upload(Cdr(52, started.AddSeconds(1), 5, 10, 0x44)); // distinct upload, same reservation
        await Upload(Cdr(51, started, 5, 10, 0x44)); // correctly matched direct authorization
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        JsonElement[] receipts;
        while (true)
        {
            receipts = (await List()).EnumerateArray().ToArray();
            if (receipts.Length == 2 && receipts.All(row => row.GetProperty("status").GetInt32() == 1)) break;
            await Task.Delay(500, deadline.Token);
        }
        Assert.Equal(new[] { 51, 52 }, receipts.Select(r => r.GetProperty("callSequence").GetInt32()).Order().ToArray());
        Assert.All(receipts, receipt =>
        {
            Assert.Equal(0.05m, receipt.GetProperty("amountUsd").GetDecimal());
            Assert.Equal(2, receipt.GetProperty("attemptCount").GetInt32());
            Assert.Equal("posted", receipt.GetProperty("lastResult").GetString());
            Assert.Contains(receipt.GetProperty("id").GetGuid(), bank.Captures.Values);
            var json = receipt.GetRawText();
            Assert.DoesNotContain(serviceKey, json); Assert.DoesNotContain(instrument, json); Assert.DoesNotContain("SB123456", json);
        });
        Assert.Equal(2, bank.Captures.Count);
        Assert.Equal(4, bank.CaptureRequests.Count);
        Assert.Equal(2, bank.CaptureRequests.Distinct().Count());
        await Upload(valid);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var intents = await db.MerchantCaptureIntents.Where(row => row.TerminalId == terminal.Id).ToListAsync();
            Assert.Equal(2, intents.Count);
            Assert.Equal(beforePayments, await db.PaymentTransactions.CountAsync());
            foreach (var intent in intents)
            {
                Assert.Equal(MerchantCaptureStatus.Posted, intent.Status);
                Assert.Equal(intent.Id, bank.Captures[intent.AuthorizationRequestId]);
                var upload = await db.UploadBatches.Include(row => row.Records).SingleAsync(row => row.Id == intent.UploadBatchId);
                Assert.Empty(upload.RawPayload); Assert.Empty(Assert.Single(upload.Records).RawPayload);
            }
        }

        Task<JsonElement> List() => admin.GetFromJsonAsync<JsonElement>("/api/cards/merchant-captures?terminalId=" + terminal.Id);
        byte[] Cdr(ushort sequence, DateTime start, int cents, byte seconds, byte classification)
        {
            var data = new byte[54]; data[0] = 53;
            DlogRateWire.EncodeRequest(route, false).AsSpan(3, 10).CopyTo(data.AsSpan(2));
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(27), cents);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(31), sequence);
            byte[] time = [(byte)(start.Year - 1900), (byte)start.Month, (byte)start.Day, (byte)start.Hour, (byte)start.Minute, (byte)start.Second,
                0, (byte)(seconds / 60), (byte)(seconds % 60), classification];
            time.CopyTo(data, 33); return data;
        }
        async Task Upload(byte[] payload)
        {
            var response = await Exchange(payload);
            Assert.Equal(DlogCallDetailWire.Acknowledgement, response[0]);
            Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(31)), BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(1)));
        }
        async Task<byte[]> Exchange(byte[] payload)
        {
            using var tcp = new TcpClient(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await tcp.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            var stream = tcp.GetStream();
            byte[] Encode(byte control, byte[] data) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wireId, Data = data });
            await stream.WriteAsync(Encode(0, payload), timeout.Token);
            Assert.NotEqual(0, (await ReadFrame(stream, timeout.Token)).Control & NccControl.Ack);
            var reply = await ReadFrame(stream, timeout.Token);
            await stream.WriteAsync(Encode((byte)(NccControl.Ack | (reply.Control & 3)), []), timeout.Token);
            await stream.WriteAsync(Encode(NccControl.Clr, []), timeout.Token);
            return reply.Data;
        }
    }

    [PostgresIntegrationFact]
    public async Task Legacy_mt2_without_native_quote_uses_explicit_published_rate_and_one_durable_reservation()
    {
        const string route = "3335550123";
        var key = Guid.NewGuid().ToString("N");
        var serviceKey = new string('K', 43);
        var instrument = new string('T', 43);
        var bank = new BankFixture(serviceKey, instrument);
        var customer = new Customer { Name = "Legacy merchant fixture", Code = Guid.NewGuid().ToString("N")[..12] };
        var site = new Site { CustomerId = customer.Id, Name = "Isolated test site", Code = "LAB" };
        var wireId = Guid.NewGuid().ToByteArray()[..5];
        var terminal = new Terminal { SiteId = site.Id, TerminalIdHex = Convert.ToHexString(wireId), DisplayName = "Legacy lab phone" };
        var plan = new RatePlan { CustomerId = customer.Id, Name = "Local default", Mode = RatingMode.RealTimeRated };
        var version = new RatePlanVersion { RatePlanId = plan.Id, VersionNumber = 1, Status = RatePlanVersionStatus.Published, PublishedAtUtc = DateTime.UtcNow };
        var rule = new RateRule { RatePlanVersionId = version.Id, Priority = 1, MatchKind = RateRuleMatchKind.Exact,
            Pattern = route, Outcome = RateRuleOutcome.Rated, RatePerMinuteUsd = 0.05m };
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint!).Port; probe.Stop();
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Ncc:Port", port.ToString())
            .UseSetting("CardPayments:ShadyBankAuthorization:Enabled", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:ServiceUrl", "http://127.0.0.1:8022/api/merchant/authorize")
            .UseSetting("CardPayments:ShadyBankAuthorization:AllowLoopbackHttp", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:ServiceKey", serviceKey)
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:TerminalId", terminal.Id.ToString())
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:Destination", route)
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:InstrumentToken", instrument)
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:AllowAuthorizationOnlyRate", "true")
            .ConfigureServices(services => services.AddHttpClient("shadybank-authorization").ConfigurePrimaryHttpMessageHandler(() => bank)));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(host, "legacy-bank-fixture", "Admin");
        admin.DefaultRequestHeaders.Add("X-API-Key", key);
        int beforePayments;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Contains("Npgsql", db.Database.ProviderName!);
            db.AddRange(customer, site, terminal, plan, version, rule);
            await db.SaveChangesAsync(); plan.PublishedVersionId = version.Id; await db.SaveChangesAsync();
            beforePayments = await db.PaymentTransactions.CountAsync();
        }

        var request = ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 101);
        var wrongCard = request.ToArray(); wrongCard[13] ^= 1;
        Assert.Equal(1, (await Exchange(wrongCard))[1]);
        Assert.Equal(1, (await Exchange(ManualCardLabTests.SyntheticAuthorizationRequest("3335550124", 2, 101)))[1]);
        Assert.Equal(1, (await Exchange(ManualCardLabTests.SyntheticAuthorizationRequest(route, 1, 101)))[1]);
        Assert.Empty(bank.RequestIds);

        var approved = await Exchange(request, retransmit: true);
        Assert.Equal(0, approved[1]); Assert.Equal("SB123456"u8.ToArray(), approved[2..10]);
        Assert.Single(bank.RequestIds);
        Assert.Equal(approved, await Exchange(request));
        var retries = await Task.WhenAll(Exchange(request), Exchange(request));
        Assert.All(retries, retry => Assert.Equal(approved, retry));
        Assert.Single(bank.RequestIds.Distinct()); Assert.Single(bank.Reservations);
        var newSequence = ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 104);
        var simultaneous = await Task.WhenAll(Exchange(newSequence), Exchange(newSequence));
        Assert.All(simultaneous, reply => Assert.Equal(approved, reply));
        Assert.Equal(2, bank.RequestIds.Distinct().Count()); Assert.Equal(2, bank.Reservations.Count);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.False(await db.CallRecords.AnyAsync(row => row.TerminalId == terminal.Id));
            Assert.Equal(beforePayments, await db.PaymentTransactions.CountAsync());
            var claims = await db.AuditEvents.Where(row => row.TerminalId == terminal.Id && row.Action == "merchant_authorization_sequence_claim").ToListAsync();
            Assert.Equal(2, claims.Count);
            var claim = Assert.Single(claims, row => row.DetailJson.Contains("\"callSequence\":101"));
            using var detail = JsonDocument.Parse(claim.DetailJson);
            Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("nativeQuoteId").ValueKind);
            Assert.Equal(version.Id, detail.RootElement.GetProperty("ratePlanVersionId").GetGuid());
            Assert.Equal(0.05m, detail.RootElement.GetProperty("amount").GetDecimal());
            Assert.DoesNotContain(instrument, claim.DetailJson); Assert.DoesNotContain(serviceKey, claim.DetailJson);
            // A blocked published route must not create another reservation in the absence of a native quote.
            var persistedRule = await db.RateRules.SingleAsync(row => row.Id == rule.Id);
            persistedRule.Outcome = RateRuleOutcome.Block; await db.SaveChangesAsync();
        }
        Assert.Equal(1, (await Exchange(ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 102)))[1]);
        Assert.Equal(6, bank.RequestIds.Count);

        // A subsequent real MT63 is saved normally, but cannot replace the earlier sequence's bank UUID.
        var response = await Exchange(DlogRateWire.EncodeRequest(route, false));
        Assert.Equal(64, response[0]);
        Assert.Equal(approved, await Exchange(request));
        Assert.Equal(2, bank.RequestIds.Distinct().Count()); Assert.Equal(2, bank.Reservations.Count);
        Assert.Equal(1, (await Exchange(ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 103)))[1]);
        var history = await admin.GetFromJsonAsync<JsonElement>("/api/cards/authorization-attempts?terminalId=" + terminal.Id);
        Assert.Contains(history.EnumerateArray(), row => row.GetProperty("outcome").GetString() == "approved" &&
            row.GetProperty("rateSource").GetString() == "authorization_only" &&
            row.GetProperty("ratePlanVersionId").GetGuid() == version.Id && row.GetProperty("quoteId").ValueKind == JsonValueKind.Null);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(1, await db.CallRecords.CountAsync(row => row.TerminalId == terminal.Id));
            Assert.Equal(beforePayments, await db.PaymentTransactions.CountAsync());
            var attempts = await db.AuditEvents.Where(row => row.TerminalId == terminal.Id && row.Action == "card_authorization_decision").ToListAsync();
            Assert.Contains(attempts, row => row.DetailJson.Contains("\"rateSource\":\"authorization_only\"") && row.DetailJson.Contains("\"outcome\":\"approved\""));
            Assert.All(attempts, row => { Assert.DoesNotContain(serviceKey, row.DetailJson); Assert.DoesNotContain(instrument, row.DetailJson); Assert.DoesNotContain("SB123456", row.DetailJson); });
            var expiring = await db.AuditEvents.SingleAsync(row => row.TerminalId == terminal.Id && row.Action == "merchant_authorization_sequence_claim" &&
                row.DetailJson.Contains("\"callSequence\":101"));
            var metadata = System.Text.Json.Nodes.JsonNode.Parse(expiring.DetailJson)!;
            metadata["quotedAtUtc"] = DateTime.UtcNow.AddMinutes(-6);
            expiring.DetailJson = metadata.ToJsonString(); await db.SaveChangesAsync();
        }
        var countBeforeExpiredRetry = bank.RequestIds.Count;
        Assert.Equal(1, (await Exchange(request))[1]);
        Assert.Equal(countBeforeExpiredRetry, bank.RequestIds.Count);

        async Task<byte[]> Exchange(byte[] payload, bool retransmit = false)
        {
            using var tcp = new TcpClient(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await tcp.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            var stream = tcp.GetStream();
            byte[] Encode(byte control, byte[] data) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wireId, Data = data });
            await stream.WriteAsync(Encode(0, payload), deadline.Token);
            Assert.NotEqual(0, (await ReadFrame(stream, deadline.Token)).Control & NccControl.Ack);
            var reply = await ReadFrame(stream, deadline.Token);
            if (retransmit)
            {
                await stream.WriteAsync(Encode(NccControl.ReTrans, payload), deadline.Token);
                Assert.NotEqual(0, (await ReadFrame(stream, deadline.Token)).Control & NccControl.Ack);
                Assert.Equal(reply.Data, (await ReadFrame(stream, deadline.Token)).Data);
            }
            await stream.WriteAsync(Encode((byte)(NccControl.Ack | (reply.Control & 3)), []), deadline.Token);
            await stream.WriteAsync(Encode(NccControl.Clr, []), deadline.Token);
            return reply.Data;
        }
    }

    [ManualShadyBankFact]
    public Task Isolated_real_bank_reservation_replays_the_same_ncc_authorization() => RunIsolatedBankAsync(capture: false);

    [ManualShadyBankCaptureFact]
    public Task Isolated_real_bank_posts_one_matching_synthetic_call_fixture() => RunIsolatedBankAsync(capture: true);

    private async Task RunIsolatedBankAsync(bool capture)
    {
        const string route = "3335550123";
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("COINLINE_SHADYBANK_LAB_SETTINGS")!));
        var serviceKey = settings.RootElement.GetProperty("merchant_service_key").GetString()!;
        var instrument = settings.RootElement.GetProperty("instrument_token").GetString()!;
        var url = new Uri(Environment.GetEnvironmentVariable("COINLINE_SHADYBANK_LAB_URL")!);
        Assert.True((url.IsLoopback || capture && url.Scheme == Uri.UriSchemeHttps) && url.AbsolutePath == "/api/merchant/authorize");
        var evidencePath = Environment.GetEnvironmentVariable("COINLINE_SHADYBANK_LAB_RESULT")!;
        var customer = new Customer { Name = "Isolated merchant bench", Code = Guid.NewGuid().ToString("N")[..12] };
        var site = new Site { CustomerId = customer.Id, Name = "Synthetic site", Code = "LAB" };
        var wireId = Guid.NewGuid().ToByteArray()[..5];
        var terminal = new Terminal { SiteId = site.Id, TerminalIdHex = Convert.ToHexString(wireId), DisplayName = "Isolated merchant phone" };
        var plan = new RatePlan { CustomerId = customer.Id, Name = "Synthetic merchant quote", Mode = RatingMode.RealTimeRated };
        var version = new RatePlanVersion { RatePlanId = plan.Id, VersionNumber = 1, Status = RatePlanVersionStatus.Published, PublishedAtUtc = DateTime.UtcNow };
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint!).Port; probe.Stop();
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", Guid.NewGuid().ToString("N"))
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Ncc:Port", port.ToString())
            .UseSetting("CardPayments:ShadyBankAuthorization:Enabled", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:ServiceUrl", url.AbsoluteUri)
            .UseSetting("CardPayments:ShadyBankAuthorization:AllowLoopbackHttp", "true")
            .UseSetting("CardPayments:ShadyBankAuthorization:ServiceKey", serviceKey)
            .UseSetting("CardPayments:ShadyBankAuthorization:CaptureUploadedCalls", capture ? "true" : "false")
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:TerminalId", terminal.Id.ToString())
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:Destination", route)
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:AllowCaptureUploadedCalls", capture ? "true" : "false")
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:TerminalClockOffsetMinutes", "0")
            .UseSetting("CardPayments:ShadyBankAuthorization:Bindings:0:InstrumentToken", instrument));
        int beforePayments;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(customer, site, terminal, plan, version);
            db.RateRules.Add(new RateRule { RatePlanVersionId = version.Id, Priority = 1, MatchKind = RateRuleMatchKind.Exact,
                Pattern = route, Outcome = RateRuleOutcome.Rated, RatePerMinuteUsd = 0.05m });
            await db.SaveChangesAsync(); plan.PublishedVersionId = version.Id; await db.SaveChangesAsync();
            beforePayments = await db.PaymentTransactions.CountAsync();
        }
        var started = DateTime.UtcNow;
        var quote = await Exchange(DlogRateWire.EncodeRequest(route, false));
        Assert.Equal(64, quote[0]); Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(quote.AsSpan(4, 2)));
        Guid quoteId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            quoteId = await db.CallRecords.Where(row => row.TerminalId == terminal.Id).Select(row => row.Id).SingleAsync();
        }
        var requestId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("coinline-shadybank-authorization:" + quoteId.ToString("N"))).AsSpan(0, 16));
        // The operator can reconcile or release a reservation even if the test stops halfway through.
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new { requestId, quotedAmount = 0.05m, completed = false }));
        var authorization = ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 1);
        var reply = await Exchange(authorization);
        Assert.Equal(65, reply[0]); Assert.Equal(0, reply[1]);
        Assert.Equal("SB"u8.ToArray(), reply[2..4]);
        Assert.All(reply[4..10], digit => Assert.InRange(digit, (byte)'0', (byte)'9'));
        Assert.Equal(reply, await Exchange(authorization));
        Assert.Equal(1, (await Exchange(ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 2)))[1]);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(beforePayments, await db.PaymentTransactions.CountAsync());
            Assert.Equal(1, await db.AuditEvents.CountAsync(row => row.Id == requestId && row.Action == "merchant_authorization_claim"));
        }
        if (capture)
        {
            // Explicit isolated TCP fixture, not evidence from a physical or emulated terminal.
            var detail = new byte[54]; detail[0] = DlogCallDetailWire.CombinedDetails;
            DlogRateWire.EncodeRequest(route, false).AsSpan(3, 10).CopyTo(detail.AsSpan(2));
            BinaryPrimitives.WriteInt32LittleEndian(detail.AsSpan(27), 5);
            BinaryPrimitives.WriteUInt16LittleEndian(detail.AsSpan(31), 1);
            byte[] timestamp = [(byte)(started.Year - 1900), (byte)started.Month, (byte)started.Day, (byte)started.Hour,
                (byte)started.Minute, (byte)started.Second, 0, 0, 10, 0x44];
            timestamp.CopyTo(detail, 33);
            var cdrReply = await Exchange(detail);
            Assert.Equal(DlogCallDetailWire.Acknowledgement, cdrReply[0]);
            Assert.Equal(cdrReply, await Exchange(detail));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            MerchantCaptureIntent? receipt;
            do
            {
                await using var scope = host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                receipt = await db.MerchantCaptureIntents.AsNoTracking().SingleOrDefaultAsync(row => row.TerminalId == terminal.Id, deadline.Token);
                if (receipt?.Status == MerchantCaptureStatus.Posted) break;
                Assert.NotEqual(MerchantCaptureStatus.NeedsReview, receipt?.Status);
                await Task.Delay(500, deadline.Token);
            } while (true);
            Assert.Equal(requestId, receipt!.AuthorizationRequestId);
            Assert.Equal(0.05m, receipt.AmountUsd);
            Assert.Equal(cdrReply, await Exchange(detail));
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                Assert.Equal(1, await db.MerchantCaptureIntents.CountAsync(row => row.TerminalId == terminal.Id));
                Assert.Equal(beforePayments, await db.PaymentTransactions.CountAsync());
            }
            await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new { requestId, settlementId = receipt.Id, chargedAmount = 0.05m,
                completed = true, fixtureOnly = true, replayConfirmed = true, postedBankReceipt = true, noLocalPaymentPosted = true }));
            return;
        }
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new { requestId, quotedAmount = 0.05m, completed = true, replayConfirmed = true, noPaymentPosted = true }));

        async Task<byte[]> Exchange(byte[] payload)
        {
            using var tcp = new TcpClient(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await tcp.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            var stream = tcp.GetStream();
            byte[] Encode(byte control, byte[] data) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wireId, Data = data });
            await stream.WriteAsync(Encode(0, payload), deadline.Token);
            Assert.NotEqual(0, (await ReadFrame(stream, deadline.Token)).Control & NccControl.Ack);
            var response = await ReadFrame(stream, deadline.Token);
            await stream.WriteAsync(Encode((byte)(NccControl.Ack | (response.Control & 3)), []), deadline.Token);
            await stream.WriteAsync(Encode(NccControl.Clr, []), deadline.Token);
            return response.Data;
        }
    }

    private sealed class ManualShadyBankFactAttribute : FactAttribute
    {
        public ManualShadyBankFactAttribute()
        {
            if (new[] { "COINLINE_SHADYBANK_LAB_SETTINGS", "COINLINE_SHADYBANK_LAB_URL", "COINLINE_SHADYBANK_LAB_RESULT", PostgresIntegrationFact.ConnectionVariable }
                .Any(key => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))))
                Skip = "Explicit isolated bank settings, result path and disposable PostgreSQL database are required.";
        }
    }

    private sealed class ManualShadyBankCaptureFactAttribute : FactAttribute
    {
        public ManualShadyBankCaptureFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("COINLINE_SHADYBANK_CAPTURE_LAB") != "1" ||
                new[] { "COINLINE_SHADYBANK_LAB_SETTINGS", "COINLINE_SHADYBANK_LAB_URL", "COINLINE_SHADYBANK_LAB_RESULT", PostgresIntegrationFact.ConnectionVariable }
                    .Any(key => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))))
                Skip = "Explicit capture opt-in, independently funded synthetic bank settings and disposable PostgreSQL database are required.";
        }
    }

    private sealed class BankFixture(string key, string instrument) : HttpMessageHandler
    {
        public ConcurrentBag<Guid> RequestIds { get; } = new();
        public ConcurrentDictionary<Guid, string> Reservations { get; } = new();
        public ConcurrentBag<(Guid Request, Guid Settlement)> CaptureRequests { get; } = new();
        public ConcurrentDictionary<Guid, Guid> Captures { get; } = new();
        public bool LoseFirstCaptureReply { get; init; }
        public string Mode { get; set; } = "approve";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(key, Assert.Single(request.Headers.GetValues("X-Shadybank-Merchant-Key")));
            Assert.InRange(request.Content!.Headers.ContentLength!.Value, 1, 1024);
            Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (request.RequestUri!.AbsolutePath == "/api/merchant/capture")
            {
                Assert.Equal(new[] { "amount", "request_id", "settlement_id" }, body.EnumerateObject().Select(row => row.Name).Order().ToArray());
                Assert.Equal("0.05", body.GetProperty("amount").GetString());
                var requestId = body.GetProperty("request_id").GetGuid();
                var settlementId = body.GetProperty("settlement_id").GetGuid();
                Assert.True(Reservations.ContainsKey(requestId));
                CaptureRequests.Add((requestId, settlementId));
                var first = Captures.TryAdd(requestId, settlementId);
                Assert.Equal(settlementId, Captures[requestId]);
                if (first && LoseFirstCaptureReply) return new HttpResponseMessage(HttpStatusCode.BadGateway);
                return Reply(JsonSerializer.Serialize(new { status = "posted", settlement_id = settlementId }));
            }
            Assert.Equal("/api/merchant/authorize", request.RequestUri.AbsolutePath);
            Assert.Equal(new[] { "amount", "instrument_token", "request_id" }, body.EnumerateObject().Select(row => row.Name).Order().ToArray());
            Assert.Equal(instrument, body.GetProperty("instrument_token").GetString());
            Assert.Equal("0.05", body.GetProperty("amount").GetString());
            var id = body.GetProperty("request_id").GetGuid(); RequestIds.Add(id);
            if (Mode == "timeout") await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            if (Mode == "http") return new HttpResponseMessage(HttpStatusCode.BadGateway);
            if (Mode == "decline") return Reply("{\"status\":\"declined\"}");
            if (Mode == "invalid") return Reply("{\"status\":\"approved\",\"authorization_code\":\"bad\"}");
            Reservations.TryAdd(id, "123456");
            return Reply("{\"status\":\"approved\",\"authorization_code\":\"123456\"}");
        }
        private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static async Task<NccWirePacket> ReadFrame(NetworkStream stream, CancellationToken ct)
    {
        var head = new byte[3]; await stream.ReadExactlyAsync(head, ct);
        var frame = new byte[NccFrameLayout.GetExpectedWireLength(head[2])]; head.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(3), ct);
        var parsed = NccFrameCodec.TryDecode(frame, NccParseMode.Strict); Assert.True(parsed.Success); return parsed.Packet!;
    }
}
