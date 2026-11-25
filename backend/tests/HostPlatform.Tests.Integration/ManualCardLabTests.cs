using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HostPlatform.Api.Hosting;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Cards;
using HostPlatform.Infrastructure.Rating;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

internal sealed class ManualCardLabFact : FactAttribute
{
    public const string SettingsVariable = "COINLINE_MANUAL_CARD_LAB_SETTINGS";
    public ManualCardLabFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SettingsVariable)))
            Skip = "Manual isolated emulator acceptance requires explicit synthetic lab settings.";
    }
}

/// <summary>Manual emulator fixture, never started by the normal application or CI.</summary>
public sealed class ManualCardLabTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [ManualCardLabFact]
    public async Task Isolated_synthetic_card_call()
    {
        var path = Environment.GetEnvironmentVariable(ManualCardLabFact.SettingsVariable)!;
        var settings = JsonSerializer.Deserialize<LabSettings>(await File.ReadAllTextAsync(path), Json)
            ?? throw new InvalidOperationException("Missing synthetic lab settings.");
        settings.Validate();
        var test = new SyntheticCardFixtureProvider(settings.Destination);
        await using var host = new LabFactory(settings.Port, settings.ObserveExistingUploadOnly ? null : test);
        using var client = host.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        Guid terminalId;
        Guid accountId;
        PreparedSet? prepared = null;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Contains("InMemory", db.Database.ProviderName!);
            var customer = new Customer { Name = "Synthetic card bench", Code = "SYNTH" };
            var site = new Site { CustomerId = customer.Id, Name = "Local test", Code = "BENCH" };
            var terminal = new Terminal { SiteId = site.Id, TerminalIdHex = settings.TerminalWireId, DisplayName = "Synthetic card bench terminal" };
            var plan = new RatePlan { CustomerId = customer.Id, Name = "Synthetic reserved-destination quote", Mode = RatingMode.RealTimeRated };
            var version = new RatePlanVersion { RatePlanId = plan.Id, VersionNumber = 1, Status = RatePlanVersionStatus.Published, PublishedAtUtc = DateTime.UtcNow };
            plan.PublishedVersionId = version.Id;
            var product = new CardProduct { Name = "Synthetic test account", Code = "LABONLY", DefaultCardType = CardType.Magstripe,
                IsTestFixtureCatalogEntry = true };
            var account = new CardAccount { CardProductId = product.Id, TerminalId = terminal.Id, ResolvedCardType = CardType.Magstripe,
                CredentialTokenRef = "synthetic-fixture-reference", Balance = 1.00m };
            var balance = new CardBalance { CardAccountId = account.Id, Amount = account.Balance, Currency = "USD" };
            db.AddRange(customer, site, terminal, plan, version, product, account, balance);
            accountId = account.Id;
            foreach (var route in new[] { settings.Destination, "1" + settings.Destination })
            {
                db.RateRules.Add(new RateRule { RatePlanVersionId = version.Id, Priority = 1000, MatchKind = RateRuleMatchKind.Exact,
                    Pattern = route, Outcome = RateRuleOutcome.Rated, RatePerMinuteUsd = 0.05m });
            }
            await db.SaveChangesAsync(deadline.Token);
            foreach (var route in new[] { settings.Destination, "1" + settings.Destination })
            {
                var quote = await RatingWorkflow.QuoteAsync(db, new RatingWorkflow.QuoteRequestDto(route, RatingMode.RealTimeRated,
                    plan.Id, customer.Id, 1m, DateTime.UtcNow), deadline.Token);
                Assert.True(quote.Allowed);
                Assert.Equal(0.05m, quote.AmountUsd);
            }
            terminalId = terminal.Id;
            test.TerminalId = terminalId;
            if (!string.IsNullOrWhiteSpace(settings.TablePackageFile))
                prepared = await PrepareIsolatedTablesAsync(db, terminal, settings, deadline.Token);
        }

        // Verify the loopback listener accepts a connection before publishing readiness.
        using (var probe = new TcpClient())
            await probe.ConnectAsync(IPAddress.Loopback, settings.Port, deadline.Token);
        await WriteJson(settings.ReadyFile, new { listening = true, host = "127.0.0.1", settings.Port,
            settings.TerminalWireId, settings.Destination, quotedUsd = 0.05m, syntheticOnly = true,
            settings.ObserveExistingUploadOnly, prepared });

        // This test reports only the isolated synthetic ledger. A quote is not a settlement.
        var settled = new HashSet<ushort>();
        while (!deadline.IsCancellationRequested)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var batches = await db.DownloadBatches.AsNoTracking().Include(row => row.Items)
                .Where(row => row.TerminalId == terminalId).ToListAsync(deadline.Token);
            var quotes = await db.CallRecords.AsNoTracking().Include(row => row.Results)
                .Where(row => row.TerminalId == terminalId).ToListAsync(deadline.Token);
            var uploads = await db.UploadBatches.AsNoTracking().Include(row => row.Records)
                .Where(row => row.TerminalId == terminalId && row.IdempotencyKey.StartsWith(TerminalUploads.CallDetailPrefix))
                .ToListAsync(deadline.Token);
            var calls = uploads.SelectMany(batch => batch.Records.Select(record =>
                TerminalUploads.ReadMetadata<DlogCallDetailWire.CallDetail>(record.DecodedMetadataJson)))
                .OfType<DlogCallDetailWire.CallDetail>().ToArray();
            var observed = calls.Where(call => IsSyntheticRouteCardCall(call, settings.Destination)).ToArray();
            var completed = observed.Where(call => IsCompletedAuthorizedCall(call, test.ApprovedCallSequences)).ToArray();
            var account = await db.CardAccounts.Include(row => row.CardBalance).Include(row => row.CardProduct)
                .SingleAsync(row => row.Id == accountId, deadline.Token);
            var quoteObserved = quotes.Any(row => (row.DialedDigits == settings.Destination || row.DialedDigits == "1" + settings.Destination)
                && row.Results.Any(result => result.Amount == 0.05m && !result.Blocked));
            foreach (var call in completed.Where(call => call.AmountCharged is > 0 && !settled.Contains(call.Sequence)))
            {
                // This debit is restricted to the in-memory, explicitly synthetic catalog/account above.
                var amount = call.AmountCharged!.Value / 100m;
                if (!quoteObserved || amount > account.Balance || account.CardProduct?.IsTestFixtureCatalogEntry != true)
                    continue;
                new CardAccountLedger().ApplyBalanceAdjustment(account, account.CardProduct, -amount);
                db.BalanceAdjustments.Add(new BalanceAdjustment { CardAccountId = account.Id, Delta = -amount,
                    SimulationMode = true, Reason = $"Synthetic reported call charge, sequence {call.Sequence}" });
                await db.SaveChangesAsync(deadline.Token);
                settled.Add(call.Sequence);
            }
            var snapshot = new
            {
                syntheticOnly = true, settings.Destination, settings.ObserveExistingUploadOnly, quoteObserved,
                simulatedAccountBalanceUsd = account.Balance,
                downloads = batches.Select(batch => new { batch.Status, total = batch.Items.Count,
                    acknowledged = batch.Items.Count(item => item.Succeeded), batch.LastError }).ToArray(),
                quotes = quotes.Select(row => new { row.DialedDigits, amountsUsd = row.Results.Select(result => result.Amount).ToArray() }).ToArray(),
                denied = test.DeniedCount,
                approvedAuthorizationSequences = test.ApprovedCallSequences.Order().ToArray(),
                observedCallDetails = calls.Select(call => new { call.MessageType, call.Sequence, call.CallType,
                    call.DurationSeconds, call.Payment, matchesSyntheticRoute = IsSyntheticRouteCardCall(call, settings.Destination),
                    matchingCompletedAuthorization = completed.Contains(call), reportedChargeCents = call.AmountCharged,
                    reportedCollectionCents = call.AmountCollected }).ToArray(),
                uploadedCalls = completed.Select(call => new { call.Sequence, call.DurationSeconds, call.Payment,
                    reportedChargeCents = call.AmountCharged, reportedCollectionCents = call.AmountCollected }).ToArray(),
                simulatedCharges = completed.Where(call => settled.Contains(call.Sequence) && call.AmountCharged.HasValue)
                    .GroupBy(call => call.Sequence).Select(group => new { sequence = group.Key, chargeCents = group.First().AmountCharged }).ToArray(),
                // The fixture ledger is an observation of the reported charge, not a processor or monetary balance update.
                processorSettlement = false
            };
            await WriteJson(settings.ResultFile, snapshot);
            if (settings.ObserveExistingUploadOnly ? observed.Length > 0 : quoteObserved && completed.Length > 0 && settled.Count > 0)
                return;
            try { await Task.Delay(1000, deadline.Token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { break; }
        }
        Assert.Fail(settings.ObserveExistingUploadOnly
            ? "No test-route card detail was reported before the observation deadline. See the safe result file."
            : "No matching completed test-card call was reported before the manual fixture deadline. See the safe result file.");
    }

    private static bool IsSyntheticRouteCardCall(DlogCallDetailWire.CallDetail call, string destination) =>
        (call.DialedDigits == destination || call.DialedDigits == "1" + destination)
        && call.Payment is "Credit card" or "Calling card" or "Debit card" or "Card";

    private static bool IsCompletedAuthorizedCall(DlogCallDetailWire.CallDetail call, IReadOnlyCollection<ushort> approved) =>
        approved.Contains(call.Sequence) && call.DurationSeconds is > 0
        && call.CallType is "Local" or "Intra-area toll" or "Inter-area toll" or "International" or "Operator" or "Operator-assisted";

    [Fact]
    public void Observed_old_call_or_incomplete_attempt_cannot_match_a_new_authorization()
    {
        const string route = "3335550123";
        var detail = new DlogCallDetailWire.CallDetail(53, 2, route, false, 0,
            "Inter-area toll", "Credit card", null, 10, 5, 0);
        Assert.True(IsSyntheticRouteCardCall(detail, route));
        Assert.False(IsCompletedAuthorizedCall(detail, new ushort[] { 3 }));
        Assert.False(IsCompletedAuthorizedCall(detail with { CallType = "Unanswered" }, new ushort[] { 2 }));
        Assert.False(IsCompletedAuthorizedCall(detail with { DurationSeconds = 0 }, new ushort[] { 2 }));
        Assert.False(IsSyntheticRouteCardCall(detail with { DialedDigits = "3335550124" }, route));
        Assert.True(IsCompletedAuthorizedCall(detail, new ushort[] { 2 }));
    }

    [Fact]
    public async Task Synthetic_fixture_is_restricted_and_repeated_authorizations_share_one_reference()
    {
        const string route = "3335550123";
        var id = Guid.NewGuid();
        var provider = new SyntheticCardFixtureProvider(route) { TerminalId = id };
        var fixture = SyntheticCardFixtureProvider.MakeSyntheticRequest(route);
        var first = await provider.AuthorizeAsync(id, fixture, CancellationToken.None);
        var retry = await provider.AuthorizeAsync(id, fixture, CancellationToken.None);
        Assert.True(first.Approved);
        Assert.True(retry.Approved);
        Assert.Equal(first.AuthorizationReference.ToArray(), retry.AuthorizationReference.ToArray());
        Assert.Single(provider.ApprovedCallSequences);
        Assert.False((await provider.AuthorizeAsync(Guid.NewGuid(), fixture, CancellationToken.None)).Approved);
        var otherRoute = SyntheticCardFixtureProvider.MakeSyntheticRequest("3335550124");
        Assert.False((await provider.AuthorizeAsync(id, otherRoute, CancellationToken.None)).Approved);
        var otherCredential = fixture.ToArray();
        otherCredential[13] ^= 1;
        Assert.False((await provider.AuthorizeAsync(id, otherCredential, CancellationToken.None)).Approved);
        var otherFormat = fixture.ToArray();
        otherFormat[1] = 1;
        Assert.False((await provider.AuthorizeAsync(id, otherFormat, CancellationToken.None)).Approved);
        Assert.Equal(4, provider.DeniedCount);
        Assert.Single(provider.ApprovedCallSequences);
    }

    private sealed record LabRecord(int Number, string Name, int SortOrder, int? Dependency, string Sha256, string PayloadBase64);
    private sealed record PreparedSet(Guid TableSetId, int Records, int[] ChangedRecordNumbers);

    private static async Task<PreparedSet> PrepareIsolatedTablesAsync(HostPlatformDbContext db, Terminal terminal,
        LabSettings settings, CancellationToken ct)
    {
        var records = JsonSerializer.Deserialize<LabRecord[]>(await File.ReadAllTextAsync(settings.TablePackageFile!, ct), Json)
            ?? throw new InvalidOperationException("Synthetic table package is missing.");
        var expected = new[] { 21, 26, 31, 50, 62, 72, 73, 92, 93, 134, 135, 136 };
        if (!records.OrderBy(record => record.SortOrder).Select(record => record.Number).SequenceEqual(expected))
            throw new InvalidOperationException("The isolated baseline must contain exactly the twelve standard Multi-pay records.");
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.LaterMultiPay180)!;
        var payloads = new Dictionary<int, byte[]>();
        foreach (var record in records)
        {
            var payload = Convert.FromBase64String(record.PayloadBase64);
            if (payload.Length != profile.VerifiedLengths[record.Number] || payload[0] != record.Number
                || !Convert.ToHexString(SHA256.HashData(payload)).Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Synthetic baseline MT{record.Number} did not match its profile or saved hash.");
            payloads.Add(record.Number, payload);
        }
        var access = NativeTableLayouts.Find(21, payloads[21], profile.LayoutFor(21))!;
        var accessField = access.Sections.SelectMany(section => section.Fields).Single(field => field.Key == "terminalNumber");
        if (accessField.Read(payloads[21]) != settings.TerminalWireId)
            throw new InvalidOperationException("The synthetic baseline access record is for a different wire ID.");

        // Use the normal native editor for one unused tariff and two empty screening slots.
        // No source table is changed, and all bytes beyond those selected fields are preserved.
        var rates = NativeTableLayouts.Find(73, payloads[73], profile.LayoutFor(73))!;
        var rateFields = rates.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        if (rateFields["rate-127.classification"].Read(payloads[73]) != "0")
            throw new InvalidOperationException("The candidate synthetic tariff is no longer the expected unused slot.");
        var screening = NativeTableLayouts.Find(92, payloads[92], profile.LayoutFor(92))!;
        var screenFields = screening.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        foreach (var slot in new[] { 1, 2 })
            if (!string.IsNullOrEmpty(screenFields[$"rule-{slot}.pattern"].Read(payloads[92])))
                throw new InvalidOperationException("A required synthetic test route slot is not empty.");
        if (screening.Sections.Any(section => section.Fields.FirstOrDefault(field => field.Key.EndsWith(".pattern")) is { } pattern
                && !string.IsNullOrEmpty(pattern.Read(payloads[92]))
                && section.Fields.FirstOrDefault(field => field.Key.EndsWith(".rate"))?.Read(payloads[92]) == "127"))
            throw new InvalidOperationException("The synthetic tariff slot is referenced by an existing screening rule.");
        var changes = new Dictionary<string, string>();
        foreach (var (slot, pattern) in new[] { (1, settings.Destination), (2, "1" + settings.Destination) })
        {
            var prefix = $"rule-{slot}.";
            changes[prefix + "pattern"] = pattern;
            changes[prefix + "wildcards"] = "0";
            changes[prefix + "classification"] = "5";
            changes[prefix + "payment"] = "4";
            changes[prefix + "rate"] = "127";
            changes[prefix + "answer"] = "true";
            changes[prefix + "targetCdr"] = "true";
            foreach (var flag in new[] { "free", "carrier", "fallback", "mute", "preDialDelay", "coinException", "featureGroupB",
                "denyModemData", "denyDataPort", "restrictedAccess", "translated" })
                changes[prefix + flag] = "false";
        }
        Assert.Equal(payloads[92], screening.Apply(payloads[92], new Dictionary<string, string>()));
        Assert.Equal(payloads[73], rates.Apply(payloads[73], new Dictionary<string, string>()));
        payloads[92] = screening.Apply(payloads[92], changes);
        payloads[73] = rates.Apply(payloads[73], new Dictionary<string, string>
        {
            ["rate-127.classification"] = "7", ["rate-127.initialPeriod"] = "60", ["rate-127.initialCharge"] = "5",
            ["rate-127.additionalPeriod"] = "60", ["rate-127.additionalCharge"] = "5"
        });

        var changed = records.Where(record => !Convert.ToHexString(SHA256.HashData(payloads[record.Number]))
            .Equals(record.Sha256, StringComparison.OrdinalIgnoreCase)).Select(record => record.Number).Order().ToArray();
        Assert.Equal(new[] { 73, 92 }, changed);
        foreach (var change in changes)
            Assert.Equal(change.Value, screenFields[change.Key].Read(payloads[92]));
        Assert.Equal("7", rateFields["rate-127.classification"].Read(payloads[73]));
        var firmware = new FirmwareVersion { Label = "Later Multi-pay synthetic lab", TableProfileId = profile.Id };
        terminal.FirmwareVersionId = firmware.Id;
        var set = new TableSet { Name = "Isolated synthetic card bench", TargetFirmwareVersionId = firmware.Id };
        var definitions = records.ToDictionary(record => record.Number, record => new TableDefinition
            { TableNumber = record.Number, Name = $"Synthetic baseline MT{record.Number}" });
        db.AddRange(firmware, set);
        db.TableDefinitions.AddRange(definitions.Values);
        await db.SaveChangesAsync(ct);
        var tables = new TableDistributionService(db);
        foreach (var record in records)
            await tables.UpsertVersionAsync(set.Id, definitions[record.Number].Id, record.Number is 73 or 92 ? 2 : 1,
                payloads[record.Number], record.SortOrder, record.Dependency is { } dep ? definitions[dep].Id : null, ct);
        await tables.PublishTableSetAsync(set.Id, ct);
        await tables.AssignTableSetAsync(terminal.Id, set.Id, null, null, ct);
        var saved = await db.TableVersions.Include(row => row.TableDefinition).Include(row => row.TablePayload)
            .Where(row => row.TableSetId == set.Id).ToListAsync(ct);
        if (saved.Count != expected.Length || saved.Any(row => !TableDistributionService.GetEffectivePayload(row)
                .AsSpan().SequenceEqual(payloads[row.TableDefinition!.TableNumber])))
            throw new InvalidOperationException("Isolated table set readback did not match the prepared records.");
        return new PreparedSet(set.Id, expected.Length, changed);
    }

    private static Task WriteJson(string path, object value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json));

    private sealed record LabSettings(string TerminalWireId, string Destination, int Port, int TimeoutSeconds, string ReadyFile, string ResultFile,
        string? TablePackageFile = null, bool ObserveExistingUploadOnly = false)
    {
        public void Validate()
        {
            // The manual fixture has only reserved fictional NANP lines. It cannot route calls or process real payments.
            if (!Regex.IsMatch(TerminalWireId, "^[0-9]{3}55501[0-9]{2}$") || !Regex.IsMatch(Destination, "^[0-9]{3}55501[0-9]{2}$")
                || Port is < 1024 or > 65535 || TimeoutSeconds is < 30 or > 1800 || string.IsNullOrWhiteSpace(ReadyFile)
                || string.IsNullOrWhiteSpace(ResultFile) || Path.GetFullPath(ReadyFile) == Path.GetFullPath(ResultFile))
                throw new InvalidOperationException("Use reserved synthetic line IDs and a distinct local fixture output path.");
        }
    }

    private sealed class LabFactory(int port, SyntheticCardFixtureProvider? provider) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Ncc:Listen", "1");
            builder.UseSetting("Ncc:Port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Platform:Seed:EnableDemoData", "false");
            builder.UseSetting("SipModem:Enabled", "false");
            builder.UseSetting("SerialModem:Enabled", "false");
            builder.UseSetting("Observability:MetricsEnabled", "false");
            if (provider is not null)
                builder.ConfigureServices(services => services.AddSingleton<INccCardAuthorizationProvider>(provider));
        }
    }

    // The provider exists only in this manual test assembly. It recognizes a single widely used
    // non-production card fixture, one reserved destination and the fixture's isolated terminal.
    // It cannot contact an issuer, charge a real account or supply real card credentials.
    private sealed class SyntheticCardFixtureProvider(string destination) : INccCardAuthorizationProvider
    {
        private static readonly byte[] SyntheticCredential = [0x41, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11];
        private readonly ConcurrentDictionary<ushort, byte[]> _approved = new();
        private int _denied;
        public Guid TerminalId { get; set; }
        public int DeniedCount => Volatile.Read(ref _denied);
        public IReadOnlyCollection<ushort> ApprovedCallSequences => _approved.Keys.ToArray();

        // Only used by the automated test above; this fixture has no real card details.
        public static byte[] MakeSyntheticRequest(string route)
        {
            var data = new byte[39];
            data[0] = 2;
            Convert.FromHexString(route.PadRight(20, 'E')).CopyTo(data, 2);
            SyntheticCredential.CopyTo(data, 13);
            data[21] = data[22] = 0xEE;
            return data;
        }

        public ValueTask<NccCardAuthorizationDecision> AuthorizeAsync(Guid terminalId, ReadOnlyMemory<byte> protectedRequest,
            CancellationToken cancellationToken)
        {
            var data = protectedRequest.Span;
            if (terminalId == TerminalId && ((data.Length == 29 && data[0] == 1)
                    || (data.Length == 39 && data[0] == 2 && (data[1] & 1) == 0))
                && data.Slice(13, SyntheticCredential.Length).SequenceEqual(SyntheticCredential)
                && (data[21] >> 4) > 9)
            {
                var route = DlogRateWire.DecodeBcd(data.Slice(2, 10));
                if (route == destination || route == "1" + destination)
                {
                    var sequence = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data[^2..]);
                    var reference = _approved.GetOrAdd(sequence, value => System.Text.Encoding.ASCII.GetBytes($"T{value:D7}"));
                    return ValueTask.FromResult(new NccCardAuthorizationDecision { Approved = true,
                        AuthorizationReference = data[0] == 2 ? reference : ReadOnlyMemory<byte>.Empty });
                }
            }
            Interlocked.Increment(ref _denied);
            return ValueTask.FromResult(new NccCardAuthorizationDecision());
        }
    }
}
