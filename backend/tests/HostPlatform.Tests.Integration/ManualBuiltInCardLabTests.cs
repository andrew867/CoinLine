using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Rating;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HostPlatform.Tests.Integration;

internal sealed class ManualBuiltInCardLabFact : FactAttribute
{
    public const string SettingsVariable = "COINLINE_MANUAL_BUILTIN_CARD_LAB_SETTINGS";
    public const string ConnectionVariable = "COINLINE_CARD_LAB_POSTGRES_CONNECTION";
    public ManualBuiltInCardLabFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SettingsVariable)))
            Skip = "Native isolated card acceptance requires explicit private synthetic lab settings.";
    }
}

public sealed partial class ManualCardLabTests
{
    /// <summary>
    /// Opt-in native emulator acceptance using the actual built-in provider and
    /// isolated persistent ledger, not the legacy in-memory fixture provider.
    /// </summary>
    [ManualBuiltInCardLabFact]
    public async Task Native_emulator_call_using_built_in_synthetic_provider()
    {
        var settingsPath = Environment.GetEnvironmentVariable(ManualBuiltInCardLabFact.SettingsVariable)!;
        var options = JsonSerializer.Deserialize<BuiltInLabSettings>(await File.ReadAllTextAsync(settingsPath), Json)
            ?? throw new InvalidOperationException("Missing built-in synthetic lab settings.");
        options.Validate();
        var settings = options.Lab;
        var connection = ValidateLabConnection(Environment.GetEnvironmentVariable(ManualBuiltInCardLabFact.ConnectionVariable));
        await RequireEmptyLabDatabase(connection);
        RequireFreeLoopbackPort(settings.Port);
        var terminal = new Terminal { TerminalIdHex = settings.TerminalWireId, DisplayName = "Synthetic native card bench" };
        var product = new CardProduct { Name = "Synthetic native lab fixture", Code = "NATIVELAB", DefaultCardType = CardType.Magstripe,
            IsTestFixtureCatalogEntry = true };
        var account = new CardAccount { TerminalId = terminal.Id, CardProductId = product.Id, ResolvedCardType = CardType.Magstripe,
            CredentialKind = CardCredentialKind.TestFixture, CredentialTokenRef = "native-synthetic-fixture", Balance = 1m };
        await using var host = new BuiltInLabFactory(options, connection.ConnectionString, terminal.Id, account.Id);
        using var client = host.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        PreparedSet? prepared = null;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(db.Database.IsNpgsql());
            var customer = new Customer { Name = "Synthetic native lab", Code = "NATIVELAB" };
            var site = new Site { CustomerId = customer.Id, Name = "Loopback test", Code = "BENCH" };
            terminal.SiteId = site.Id;
            var plan = new RatePlan { CustomerId = customer.Id, Name = "Synthetic reserved route", Mode = RatingMode.RealTimeRated };
            var version = new RatePlanVersion { RatePlanId = plan.Id, VersionNumber = 1,
                Status = RatePlanVersionStatus.Published, PublishedAtUtc = DateTime.UtcNow };
            db.AddRange(customer, site, terminal, product, account, plan, version,
                new CardBalance { CardAccountId = account.Id, Amount = account.Balance, Currency = "USD" });
            foreach (var route in new[] { settings.Destination, "1" + settings.Destination })
                db.RateRules.Add(new RateRule { RatePlanVersionId = version.Id, Priority = 1000, MatchKind = RateRuleMatchKind.Exact,
                    Pattern = route, Outcome = RateRuleOutcome.Rated, RatePerMinuteUsd = 0.05m });
            await db.SaveChangesAsync(deadline.Token);
            plan.PublishedVersionId = version.Id;
            await db.SaveChangesAsync(deadline.Token);
            foreach (var route in new[] { settings.Destination, "1" + settings.Destination })
            {
                var quote = await RatingWorkflow.QuoteAsync(db, new RatingWorkflow.QuoteRequestDto(route,
                    RatingMode.RealTimeRated, plan.Id, customer.Id, 1m, DateTime.UtcNow), deadline.Token);
                Assert.True(quote.Allowed);
                Assert.Equal(0.05m, quote.AmountUsd);
            }
            if (!string.IsNullOrWhiteSpace(settings.TablePackageFile))
                prepared = await PrepareIsolatedTablesAsync(db, terminal, settings, deadline.Token);
        }

        using (var probe = new TcpClient())
            await probe.ConnectAsync(IPAddress.Loopback, settings.Port, deadline.Token);
        var probeObserved = false;
        for (var attempt = 0; attempt < 50 && !probeObserved; attempt++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            probeObserved = await db.NccSessions.AsNoTracking().AnyAsync(deadline.Token);
            if (!probeObserved) await Task.Delay(100, deadline.Token);
        }
        Assert.True(probeObserved, "The isolated database did not observe the loopback NCC readiness probe.");
        await WriteLabSnapshot(settings.ReadyFile, new
        {
            listening = true, host = "127.0.0.1", settings.Port, settings.Destination,
            quotedUsd = 0.05m, syntheticOnly = true, provider = "built-in",
            options.RequireCompletedCdr, options.EnableCdrPosting, prepared
        });

        var observedApproval = false;
        var observedMatchingCdr = false;
        var observedSimulatedPayment = false;
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                await using var scope = host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var quotes = await db.CallRecords.AsNoTracking().Include(row => row.Results)
                    .Where(row => row.TerminalId == terminal.Id).ToListAsync(deadline.Token);
                var approveAudit = await db.AuditEvents.AsNoTracking().Where(row => row.TerminalId == terminal.Id
                    && row.Category == "ncc" && row.Action == "card_authorization_decision").ToListAsync(deadline.Token);
                // Only read typed synthetic audit data. Never include raw frames,
                // authorization references, account credential fields or dialed data in the result.
                var approvals = approveAudit.Select(row => ReadApproval(row.DetailJson, row.CreatedAtUtc))
                    .OfType<NativeApproval>().Where(row => row.CardAccountId == account.Id).ToArray();
                var quoteObserved = quotes.Any(row => row.DialedDigits == settings.Destination || row.DialedDigits == "1" + settings.Destination);
                var approved = approvals.Where(row => quotes.Any(quote => quote.Id == row.QuoteId && quote.Results.Any(result =>
                    result.Amount == 0.05m && !result.Blocked))).ToArray();
                observedApproval = quoteObserved && approved.Length > 0;
                var uploads = await db.UploadBatches.AsNoTracking().Include(row => row.Records)
                    .Where(row => row.TerminalId == terminal.Id && row.IdempotencyKey.StartsWith(TerminalUploads.CallDetailPrefix))
                    .ToListAsync(deadline.Token);
                var calls = uploads.SelectMany(batch => batch.Records.Select(record =>
                    TerminalUploads.ReadMetadata<DlogCallDetailWire.CallDetail>(record.DecodedMetadataJson)))
                    .OfType<DlogCallDetailWire.CallDetail>().ToArray();
                var candidates = calls.Where(call => IsSyntheticRouteCardCall(call, settings.Destination)
                    && approved.Count(approval => quotes.Any(quote => quote.Id == approval.QuoteId
                        && MatchesNativeCdr(call, approval, quote.StartedAtUtc, options.TerminalClockOffsetMinutes))) == 1).ToArray();
                observedMatchingCdr = candidates.Length > 0;
                var payments = await db.PaymentTransactions.AsNoTracking().Where(row => row.CardAccountId == account.Id)
                    .Select(row => new { row.Amount, row.Reconciliation }).ToArrayAsync(deadline.Token);
                var adjustments = await db.BalanceAdjustments.AsNoTracking().Where(row => row.CardAccountId == account.Id)
                    .Select(row => row.Delta).ToArrayAsync(deadline.Token);
                var balance = await db.CardAccounts.AsNoTracking().Where(row => row.Id == account.Id)
                    .Select(row => row.Balance).SingleAsync(deadline.Token);
                observedSimulatedPayment = payments.Length == 1 && payments[0].Amount == -0.05m
                    && payments[0].Reconciliation == ReconciliationStatus.Pending && adjustments.SequenceEqual([-0.05m]) && balance == 0.95m;
                if (!options.EnableCdrPosting)
                    Assert.True(payments.Length == 0 && adjustments.Length == 0 && balance == 1m,
                        "Authorization or observation alone must leave the isolated ledger unchanged.");
                else
                    Assert.True(payments.Length <= 1 && adjustments.Length <= 1, "The isolated one-call ledger posted more than once.");
                var activeNccSessions = await db.NccSessions.AsNoTracking().CountAsync(row => row.TerminalId == terminal.Id
                    && row.Status == NccSessionStatus.Active, deadline.Token);
                var batches = await db.DownloadBatches.AsNoTracking().Include(row => row.Items)
                    .Where(row => row.TerminalId == terminal.Id).ToListAsync(deadline.Token);
                await WriteLabSnapshot(settings.ResultFile, new
                {
                    syntheticOnly = true, provider = "built-in", options.RequireCompletedCdr, options.EnableCdrPosting,
                    quoteObserved, observedApproval, observedMatchingCdr, observedSimulatedPayment, simulatedBalanceUsd = balance,
                    activeNccSessions, approvalCount = approved.Length, approvedCallSequences = approved.Select(row => row.CallSequence).Order().ToArray(),
                    downloads = batches.Select(batch => new { batch.Status, total = batch.Items.Count,
                        acknowledged = batch.Items.Count(item => item.Succeeded) }).ToArray(),
                    observedCallDetails = calls.Select(call => new { call.MessageType, call.Sequence, call.CallType,
                        call.DurationSeconds, call.Payment, matchesReservedRoute = IsSyntheticRouteCardCall(call, settings.Destination),
                        matchingAuthorizedCandidate = candidates.Contains(call), reportedChargeCents = call.AmountCharged,
                        reportedCollectionCents = call.AmountCollected }).ToArray(),
                    simulatedPayments = payments, processorSettlement = false
                });
                // The finish marker is an operator signal only. It is not a
                // substitute for an observed authorization or uploaded record.
                if (File.Exists(options.FinishFile) && activeNccSessions == 0)
                    break;
                await Task.Delay(1000, deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { break; }
        }

        Assert.True(observedApproval, "No matching native authorization was observed. See the redacted result file.");
        if (options.RequireCompletedCdr)
            Assert.True(observedMatchingCdr, "No matching native completed call record was observed. See the redacted result file.");
        if (options.EnableCdrPosting)
            Assert.True(observedSimulatedPayment, "No matching simulated ledger posting was observed. See the redacted result file.");
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Database=coinline_card_lab_fixture;Username=fixture", true)]
    [InlineData("Host=localhost;Database=coinline_card_lab_fixture_2;Username=fixture", true)]
    [InlineData("Host=localhost;Database=coinline;Username=fixture", false)]
    [InlineData("Host=fixture.invalid;Database=coinline_card_lab_fixture;Username=fixture", false)]
    [InlineData("Host=localhost;Database=coinline_card_lab_fixture", false)]
    public void Native_lab_connection_rejects_nonlocal_or_unmarked_database(string connection, bool accepted)
    {
        if (accepted)
            Assert.NotNull(ValidateLabConnection(connection));
        else
            Assert.Throws<InvalidOperationException>(() => ValidateLabConnection(connection));
    }

    [Fact]
    public void Native_record_candidate_requires_matching_sequence_completion_and_explicit_clock()
    {
        var quote = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var approval = new NativeApproval(Guid.NewGuid(), Guid.NewGuid(), 7, quote.AddSeconds(10));
        var call = new DlogCallDetailWire.CallDetail(53, 7, "3335550123", false, 0, "Inter-area toll", "Credit card",
            new DateTime(2026, 1, 2, 8, 30, 20), 10, 5, 0);
        Assert.True(MatchesNativeCdr(call, approval, quote, -210));
        Assert.False(MatchesNativeCdr(call, approval, quote, null));
        Assert.False(MatchesNativeCdr(call, approval, quote, 0));
        Assert.False(MatchesNativeCdr(call with { Sequence = 8 }, approval, quote, -210));
        Assert.False(MatchesNativeCdr(call with { DurationSeconds = 0 }, approval, quote, -210));
        Assert.False(MatchesNativeCdr(call with { TerminalStart = call.TerminalStart!.Value.AddDays(-1) }, approval, quote, -210));
    }

    private sealed record NativeApproval(Guid CardAccountId, Guid QuoteId, ushort CallSequence, DateTime DecisionAtUtc);

    private static bool MatchesNativeCdr(DlogCallDetailWire.CallDetail call, NativeApproval approval, DateTime quoteTime, int? offset)
    {
        if (offset is not (>= -840 and <= 840) || call.TerminalStart is not { } start
            || !IsCompletedAuthorizedCall(call, new[] { approval.CallSequence }))
            return false;
        try
        {
            var utc = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), TimeSpan.FromMinutes(offset.Value)).UtcDateTime;
            return utc >= quoteTime.AddSeconds(-30) && utc <= approval.DecisionAtUtc.AddMinutes(5);
        }
        catch (ArgumentException) { return false; }
    }

    private static NativeApproval? ReadApproval(string json, DateTime timestamp)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("provider", out var provider) && provider.GetString() == "synthetic_lab"
                && root.TryGetProperty("outcome", out var outcome) && outcome.GetString() == "approved"
                && root.TryGetProperty("cardAccountId", out var card) && card.TryGetGuid(out var accountId)
                && root.TryGetProperty("quoteId", out var quote) && quote.TryGetGuid(out var quoteId)
                && root.TryGetProperty("callSequence", out var sequence) && sequence.TryGetUInt16(out var number)
                ? new NativeApproval(accountId, quoteId, number, timestamp) : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }

    private static async Task WriteLabSnapshot(string path, object value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await WriteJson(temporary, value);
        File.Move(temporary, path, overwrite: true);
    }

    private static NpgsqlConnectionStringBuilder ValidateLabConnection(string? input)
    {
        NpgsqlConnectionStringBuilder connection;
        try { connection = new NpgsqlConnectionStringBuilder(input ?? ""); }
        catch (Exception error) when (error is ArgumentException or FormatException)
        { throw new InvalidOperationException("Use an explicit isolated local PostgreSQL lab connection."); }
        if (connection.Host is not ("127.0.0.1" or "localhost") ||
            !Regex.IsMatch(connection.Database ?? "", "^coinline_card_lab_[a-z0-9_]+$") ||
            string.IsNullOrWhiteSpace(connection.Username))
            throw new InvalidOperationException("The native card lab requires a loopback PostgreSQL database named coinline_card_lab_<unique>.");
        return connection;
    }

    private static async Task RequireEmptyLabDatabase(NpgsqlConnectionStringBuilder connection)
    {
        await using var database = new NpgsqlConnection(connection.ConnectionString);
        await database.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname NOT IN ('pg_catalog','information_schema')
                    AND n.nspname NOT LIKE 'pg_%' AND c.relkind IN ('r','p','m','v','f')
            )
            """, database);
        if (await command.ExecuteScalarAsync() is not false)
            throw new InvalidOperationException("The named lab database is not empty. Use a new isolated database; nothing was removed.");
    }

    private static void RequireFreeLoopbackPort(int port)
    {
        var probe = new TcpListener(IPAddress.Loopback, port);
        try { probe.Start(); }
        catch (SocketException) { throw new InvalidOperationException("The selected native lab listener port is already in use."); }
        finally { probe.Stop(); }
    }

    private sealed record BuiltInLabSettings(LabSettings Lab, string FinishFile, bool RequireCompletedCdr = true,
        bool EnableCdrPosting = false, int? TerminalClockOffsetMinutes = null)
    {
        public void Validate()
        {
            if (Lab == null || string.IsNullOrWhiteSpace(FinishFile))
                throw new InvalidOperationException("Supply private native lab settings and a distinct finish-marker path.");
            Lab.Validate();
            var paths = new[] { Lab.ReadyFile, Lab.ResultFile, FinishFile }.Select(Path.GetFullPath).ToArray();
            if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length
                || paths.Any(path => File.Exists(path) || Directory.Exists(path) || !Directory.Exists(Path.GetDirectoryName(path)))
                || Lab.ObserveExistingUploadOnly || Lab.Destination[0] is < '2' or > '9' || Lab.TerminalWireId[0] is < '2' or > '9'
                || RequireCompletedCdr && TerminalClockOffsetMinutes is not (>= -840 and <= 840)
                || EnableCdrPosting && !RequireCompletedCdr)
                throw new InvalidOperationException("Use new distinct lab result/finish paths and an explicit terminal clock offset for a completed CDR trial.");
        }
    }

    private sealed class BuiltInLabFactory(BuiltInLabSettings options, string connection, Guid phone, Guid account) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder web)
        {
            web.UseEnvironment("Production")
                .UseSetting("ConnectionStrings:Default", connection)
                .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", Guid.NewGuid().ToString("N"))
                .UseSetting("Platform:Seed:EnableDemoData", "false")
                .UseSetting("Platform:PayloadStorage:RootPath", "")
                .UseSetting("Observability:MetricsEnabled", "false")
                .UseSetting("SipModem:Enabled", "false").UseSetting("SerialModem:Enabled", "false")
                .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Ncc:Port", options.Lab.Port.ToString())
                .UseSetting("CardPayments:LabAuthorization:Enabled", "true")
                .UseSetting("CardPayments:LabAuthorization:Bindings:0:TerminalId", phone.ToString())
                .UseSetting("CardPayments:LabAuthorization:Bindings:0:CardAccountId", account.ToString())
                .UseSetting("CardPayments:LabAuthorization:Bindings:0:Destination", options.Lab.Destination)
                .UseSetting("CardPayments:LabAuthorization:SettleUploadedCalls", options.EnableCdrPosting.ToString());
            if (options.TerminalClockOffsetMinutes is { } offset)
                web.UseSetting("CardPayments:LabAuthorization:Bindings:0:TerminalClockOffsetMinutes", offset.ToString());
        }
    }
}
