using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresSyntheticAuthorizationTests
{
    [PostgresIntegrationFact]
    public async Task Native_quote_and_test_authorizations_are_audited_and_only_matched_cdr_posts_once()
    {
        const string route = "3335550123";
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var customer = new Customer { Name = "Synthetic authorization customer", Code = Guid.NewGuid().ToString("N")[..12] };
        var site = new Site { CustomerId = customer.Id, Name = "Isolated test site", Code = "LAB" };
        var wireId = Guid.NewGuid().ToByteArray()[..5];
        var otherWireId = Guid.NewGuid().ToByteArray()[..5];
        var terminal = new Terminal { SiteId = site.Id, TerminalIdHex = Convert.ToHexString(wireId), DisplayName = "Synthetic authorization phone" };
        var otherTerminal = new Terminal { SiteId = site.Id, TerminalIdHex = Convert.ToHexString(otherWireId), DisplayName = "Unflagged fixture phone" };
        var product = new CardProduct { Name = "Approved lab fixture", Code = Guid.NewGuid().ToString("N")[..12], DefaultCardType = CardType.Magstripe, IsTestFixtureCatalogEntry = true };
        var otherProduct = new CardProduct { Name = "Non-test product", Code = Guid.NewGuid().ToString("N")[..12], DefaultCardType = CardType.Magstripe };
        var account = Account(product.Id, terminal.Id);
        var otherAccount = Account(otherProduct.Id, otherTerminal.Id);
        var plan = new RatePlan { CustomerId = customer.Id, Name = "Synthetic rate plan", Mode = RatingMode.RealTimeRated };
        var version = new RatePlanVersion { RatePlanId = plan.Id, VersionNumber = 1, Status = RatePlanVersionStatus.Published, PublishedAtUtc = DateTime.UtcNow };
        var legacySession = Guid.NewGuid();
        var reader = new OperatorAccount { Username = "lab-reader-" + Guid.NewGuid().ToString("N"), DisplayName = "Lab reader", Role = OperatorRole.Operator,
            PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = "{\"card-transactions\":1}" };
        var otherReader = new OperatorAccount { Username = "lab-other-" + Guid.NewGuid().ToString("N"), DisplayName = "Other reader", Role = OperatorRole.Operator,
            PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = "{\"phones\":1}" };
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Listen", "1")
            .UseSetting("Ncc:Port", port.ToString())
            .UseSetting("CardPayments:LabAuthorization:Enabled", "true")
            .UseSetting("CardPayments:LabAuthorization:SettleUploadedCalls", "true")
            .UseSetting("CardPayments:LabAuthorization:Bindings:0:TerminalId", terminal.Id.ToString())
            .UseSetting("CardPayments:LabAuthorization:Bindings:0:CardAccountId", account.Id.ToString())
            .UseSetting("CardPayments:LabAuthorization:Bindings:0:Destination", route)
            .UseSetting("CardPayments:LabAuthorization:Bindings:0:TerminalClockOffsetMinutes", "0")
            .UseSetting("CardPayments:LabAuthorization:Bindings:1:TerminalId", otherTerminal.Id.ToString())
            .UseSetting("CardPayments:LabAuthorization:Bindings:1:CardAccountId", otherAccount.Id.ToString())
            .UseSetting("CardPayments:LabAuthorization:Bindings:1:Destination", route));
        using var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Add("X-API-Key", key);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Contains("Npgsql", db.Database.ProviderName!);
            db.AddRange(customer, site, terminal, otherTerminal, product, otherProduct, account, otherAccount,
                new CardBalance { CardAccountId = account.Id, Amount = 1m, Currency = "USD" },
                new CardBalance { CardAccountId = otherAccount.Id, Amount = 1m, Currency = "USD" }, reader, otherReader, plan, version);
            db.RateRules.Add(new RateRule { RatePlanVersionId = version.Id, Priority = 1, MatchKind = RateRuleMatchKind.Exact,
                Pattern = route, Outcome = RateRuleOutcome.Rated, RatePerMinuteUsd = 0.05m });
            for (var i = 0; i < 2; i++)
                db.AuditEvents.Add(new AuditEvent { Category = "ncc", Action = "frame_stored", TerminalId = terminal.Id,
                    TerminalSessionId = legacySession.ToString(), DetailJson = "{\"redactedMessageType\":2,\"untrusted\":\"must-not-return\"}" });
            await db.SaveChangesAsync();
            plan.PublishedVersionId = version.Id;
            await db.SaveChangesAsync();
        }

        var state = await admin.GetFromJsonAsync<JsonElement>("/api/cards/simulation-state");
        Assert.True(state.GetProperty("runtimeAuthorizationProviderRegistered").GetBoolean());
        Assert.True(state.GetProperty("labAuthorizationEnabled").GetBoolean());
        Assert.True(state.GetProperty("labSettlementEnabled").GetBoolean());
        var request = ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 1);
        await Denied(wireId, request); // no preceding quote
        await Denied(otherWireId, request); // a non-test product cannot authorize
        var unknown = request.ToArray();
        unknown[13] ^= 1;
        await Denied(wireId, unknown);

        await Quote();
        var approved = await Exchange(wireId, request);
        Assert.Equal(65, approved[0]);
        Assert.Equal(0, approved[1]);
        Assert.Equal(approved, await Exchange(wireId, request));
        await Denied(wireId, ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 2));

        await Quote();
        var formatted = ManualCardLabTests.SyntheticAuthorizationRequest(route, 1, 3);
        var concurrent = await Task.WhenAll(Exchange(wireId, formatted), Exchange(wireId, formatted));
        Assert.All(concurrent, response => Assert.Equal(new byte[] { 3, 0 }, response));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(1m, await db.CardAccounts.Where(row => row.Id == account.Id).Select(row => row.Balance).SingleAsync());
            Assert.Equal(1m, await db.CardBalances.Where(row => row.CardAccountId == account.Id).Select(row => row.Amount).SingleAsync());
            Assert.False(await db.PaymentTransactions.AnyAsync(row => row.CardAccountId == account.Id));
            var storedAccount = await db.CardAccounts.Include(row => row.CardBalance).SingleAsync(row => row.Id == account.Id);
            storedAccount.Balance = storedAccount.CardBalance!.Amount = 0.01m;
            await db.SaveChangesAsync();
        }
        await Quote();
        await Denied(wireId, ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 4));

        var path = "/api/cards/authorization-attempts?terminalId=" + terminal.Id;
        using var restricted = host.CreateClient();
        restricted.DefaultRequestHeaders.Add("X-API-Key", key);
        await SignIn(restricted, reader);
        var raw = await restricted.GetStringAsync(path);
        Assert.DoesNotContain("must-not-return", raw);
        var history = JsonSerializer.Deserialize<JsonElement>(raw).EnumerateArray().ToArray();
        Assert.All(history, row => Assert.Equal(13, row.EnumerateObject().Count()));
        Assert.Equal(2, history.Count(row => Value(row, "outcome") == "approved"));
        Assert.Single(history.Where(row => Value(row, "outcome") == "unknown"));
        Assert.Single(history.Where(row => Value(row, "reason") == "quote_missing"));
        Assert.Single(history.Where(row => Value(row, "reason") == "fixture_not_recognized"));
        Assert.Single(history.Where(row => Value(row, "reason") == "quote_already_used"));
        Assert.Single(history.Where(row => Value(row, "reason") == "insufficient_balance"));
        Assert.All(history.Where(row => Value(row, "outcome") == "approved"), row =>
        {
            Assert.Equal("synthetic_lab", Value(row, "provider"));
            Assert.Equal(account.Id, row.GetProperty("cardAccountId").GetGuid());
            Assert.Equal(0.05m, row.GetProperty("quotedAmountUsd").GetDecimal());
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync("/api/cards/accounts")).StatusCode);
        using var deniedReader = host.CreateClient();
        deniedReader.DefaultRequestHeaders.Add("X-API-Key", key);
        await SignIn(deniedReader, otherReader);
        Assert.Equal(HttpStatusCode.Forbidden, (await deniedReader.GetAsync(path)).StatusCode);
        using var anonymous = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var rows = await db.AuditEvents.Where(row => row.TerminalId == terminal.Id && row.Action == "card_authorization_decision").ToListAsync();
            Assert.Equal(2, rows.Count(row => row.DetailJson.Contains("\"outcome\":\"approved\"")));
            Assert.Equal(2, await db.AuditEvents.CountAsync(row => row.TerminalId == terminal.Id && row.Action == "card_authorization_replay"));
            Assert.Equal(0.01m, await db.CardAccounts.Where(row => row.Id == account.Id).Select(row => row.Balance).SingleAsync());
            Assert.Equal(0.01m, await db.CardBalances.Where(row => row.CardAccountId == account.Id).Select(row => row.Amount).SingleAsync());
            Assert.False(await db.PaymentTransactions.AnyAsync(row => row.CardAccountId == account.Id));
            var sessionIds = rows.Select(row => Guid.Parse(row.TerminalSessionId!)).ToArray();
            var protectedFrames = await db.NccSessionFrames.Where(row => sessionIds.Contains(row.NccSessionId)
                && !row.SentByHost && (row.WireLength == 40 || row.WireLength == 50)).ToListAsync();
            Assert.NotEmpty(protectedFrames);
            Assert.All(protectedFrames, frame => Assert.Empty(frame.RawBytes));
        }

        // A new synthetic test balance and a fresh native quote. Neither
        // authorization nor unrelated/incomplete CDRs can post a payment.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var storedAccount = await db.CardAccounts.Include(row => row.CardBalance).SingleAsync(row => row.Id == account.Id);
            storedAccount.Balance = storedAccount.CardBalance!.Amount = 1m;
            await db.SaveChangesAsync();
        }
        var terminalStart = DateTime.UtcNow;
        await Quote();
        Assert.Equal(0, (await Exchange(wireId, ManualCardLabTests.SyntheticAuthorizationRequest(route, 2, 5)))[1]);
        await Upload(Cdr(53, 4, terminalStart, 5, 10, 0x44)); // wrong sequence
        await Upload(Cdr(53, 5, terminalStart.AddHours(-1), 5, 10, 0x44)); // stale
        await Upload(Cdr(53, 5, terminalStart, 6, 10, 0x44)); // amount differs from quote
        await Upload(Cdr(53, 5, terminalStart, 5, 0, 0x44)); // no duration
        await Upload(Cdr(53, 5, terminalStart, 5, 61, 0x44)); // later charge period unqualified
        await Upload(Cdr(53, 5, terminalStart, 5, 10, 0x41)); // unanswered
        await Upload(Cdr(4, 5, terminalStart, 5, 10, 0x44)); // legacy record has no confirmed charge field
        await AssertLedger(1m, 0);

        var complete = Cdr(53, 5, terminalStart, 5, 10, 0x44);
        await Task.WhenAll(Upload(complete), Upload(complete)); // reconnect and parallel retry
        await Upload(complete);
        await Upload(Cdr(53, 5, terminalStart.AddSeconds(1), 5, 10, 0x44)); // different upload, same approval
        await AssertLedger(0.95m, 1);
        var paymentList = await restricted.GetFromJsonAsync<JsonElement>("/api/cards/transactions?cardAccountId=" + account.Id);
        var listed = Assert.Single(paymentList.EnumerateArray());
        Assert.Equal("synthetic_cdr", Value(listed, "source"));
        Assert.Equal(-0.05m, listed.GetProperty("amount").GetDecimal());
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var payment = await db.PaymentTransactions.SingleAsync(row => row.CardAccountId == account.Id);
            Assert.Equal(-0.05m, payment.Amount);
            Assert.Equal(ReconciliationStatus.Pending, payment.Reconciliation);
            Assert.Equal(CardType.Magstripe, payment.ReportedCardType);
            var adjustment = await db.BalanceAdjustments.SingleAsync(row => row.CardAccountId == account.Id);
            Assert.Equal(-0.05m, adjustment.Delta);
            Assert.True(adjustment.SimulationMode);
            Assert.Equal(1, await db.AuditEvents.CountAsync(row => row.TerminalId == terminal.Id && row.Action == "synthetic_cdr_settlement"));
        }

        async Task AssertLedger(decimal expectedBalance, int expectedPayments)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(expectedBalance, await db.CardAccounts.Where(row => row.Id == account.Id).Select(row => row.Balance).SingleAsync());
            Assert.Equal(expectedBalance, await db.CardBalances.Where(row => row.CardAccountId == account.Id).Select(row => row.Amount).SingleAsync());
            Assert.Equal(expectedPayments, await db.PaymentTransactions.CountAsync(row => row.CardAccountId == account.Id));
        }

        async Task Upload(byte[] payload)
        {
            var response = await Exchange(wireId, payload);
            Assert.Equal(DlogCallDetailWire.Acknowledgement, response[0]);
            Assert.Equal(BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(payload[0] == 53 ? 31 : 23)),
                BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(1)));
        }

        byte[] Cdr(byte type, ushort sequence, DateTime start, int cents, byte seconds, byte classification)
        {
            // Only known synthetic route and ordinary call metadata, no simulated
            // credential, authorization reference or protected card fields.
            var combined = type == DlogCallDetailWire.CombinedDetails;
            var data = new byte[combined ? 54 : 35];
            data[0] = type;
            DlogRateWire.EncodeRequest(route, false).AsSpan(3, 10).CopyTo(data.AsSpan(2));
            if (combined)
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(27), cents);
            var offset = combined ? 31 : 23;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), sequence);
            byte[] time = [(byte)(start.Year - 1900), (byte)start.Month, (byte)start.Day,
                (byte)start.Hour, (byte)start.Minute, (byte)start.Second,
                0, (byte)(seconds / 60), (byte)(seconds % 60), classification];
            time.CopyTo(data, offset + 2);
            return data;
        }

        async Task Quote()
        {
            var reply = await Exchange(wireId, DlogRateWire.EncodeRequest(route, false));
            Assert.Equal(64, reply[0]);
            Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(4, 2)));
        }

        async Task Denied(byte[] id, byte[] payload)
        {
            var reply = await Exchange(id, payload);
            Assert.Equal(1, reply[1]);
        }

        async Task<byte[]> Exchange(byte[] id, byte[] payload)
        {
            using var tcp = new TcpClient();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            var stream = tcp.GetStream();
            byte[] Encode(byte control, byte[] data) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = id, Data = data });
            await stream.WriteAsync(Encode(0, payload), deadline.Token);
            var ack = await ReadFrame(stream, deadline.Token);
            Assert.NotEqual(0, ack.Control & NccControl.Ack);
            var response = await ReadFrame(stream, deadline.Token);
            await stream.WriteAsync(Encode((byte)(NccControl.Ack | (response.Control & 3)), []), deadline.Token);
            await stream.WriteAsync(Encode(NccControl.Clr, []), deadline.Token);
            return response.Data;
        }

        async Task SignIn(HttpClient client, OperatorAccount user)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password });
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Operator", Value(result, "role"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Value(result, "token"));
        }
    }

    private static CardAccount Account(Guid productId, Guid terminalId) => new()
    {
        CardProductId = productId, TerminalId = terminalId, ResolvedCardType = CardType.Magstripe,
        CredentialKind = CardCredentialKind.TestFixture, CredentialTokenRef = "fixture-only-" + Guid.NewGuid().ToString("N"), Balance = 1m
    };

    private static string? Value(JsonElement row, string key) => row.GetProperty(key).GetString();

    private static async Task<NccWirePacket> ReadFrame(NetworkStream stream, CancellationToken ct)
    {
        var head = new byte[3];
        await stream.ReadExactlyAsync(head, ct);
        var frame = new byte[NccFrameLayout.GetExpectedWireLength(head[2])];
        head.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(3), ct);
        var parsed = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(parsed.Success);
        return parsed.Packet!;
    }
}
