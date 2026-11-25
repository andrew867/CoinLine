using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Rating;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

internal sealed class ManualGraphicalLabFact : FactAttribute
{
    public const string SettingsVariable = "COINLINE_MANUAL_GRAPHICAL_LAB_SETTINGS";
    public ManualGraphicalLabFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SettingsVariable)))
            Skip = "Manual isolated graphical emulator acceptance requires explicit synthetic settings.";
    }
}

/// <summary>Separate local provisioning fixture for a graphical terminal and a fictional free service alias.</summary>
public sealed class ManualGraphicalLabTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly int[] Expected = [21, 26, 31, 62, 50, 73, 92, 93, 72, 134, 135, 136, 30, 33, 49, 131, 132, 133];

    [ManualGraphicalLabFact]
    public async Task Isolated_graphical_service_alias_install()
    {
        var settings = JsonSerializer.Deserialize<LabSettings>(await File.ReadAllTextAsync(
            Environment.GetEnvironmentVariable(ManualGraphicalLabFact.SettingsVariable)!), Json)
            ?? throw new InvalidOperationException("Missing isolated graphical settings.");
        settings.Validate();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var baseline = JsonSerializer.Deserialize<LabRecord[]>(await File.ReadAllTextAsync(settings.TablePackageFile, deadline.Token), Json)
            ?? throw new InvalidOperationException("Missing graphical baseline.");
        var prepared = PrepareRecords(settings, baseline);
        await using var host = new LabFactory(settings.Port);
        using var client = host.CreateClient();
        Guid terminalId;
        Guid setId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Contains("InMemory", db.Database.ProviderName!);
            var customer = new Customer { Name = "Synthetic graphical bench", Code = "GRAPHICS" };
            var site = new Site { CustomerId = customer.Id, Name = "Local test", Code = "BENCH" };
            var firmware = new FirmwareVersion { Label = "Graphical synthetic bench", TableProfileId = FirmwareTableProfiles.GraphicalMultiPay180 };
            var terminal = new Terminal { SiteId = site.Id, FirmwareVersionId = firmware.Id, TerminalFamilyId = "mtr20-multipay-graphical",
                TerminalIdHex = settings.TerminalWireId, DisplayName = "Synthetic graphical bench terminal" };
            var set = new TableSet { Name = "Isolated graphical service alias", TargetFirmwareVersionId = firmware.Id };
            var plan = new RatePlan { CustomerId = customer.Id, Name = "Synthetic free service alias", Mode = RatingMode.RealTimeRated };
            var version = new RatePlanVersion { RatePlanId = plan.Id, VersionNumber = 1, Status = RatePlanVersionStatus.Published, PublishedAtUtc = DateTime.UtcNow };
            plan.PublishedVersionId = version.Id;
            db.AddRange(customer, site, firmware, terminal, set, plan, version);
            foreach (var route in new[] { settings.Destination, "1" + settings.Destination })
                db.RateRules.Add(new RateRule { RatePlanVersionId = version.Id, Priority = 1000, MatchKind = RateRuleMatchKind.Exact,
                    Pattern = route, Outcome = RateRuleOutcome.Free, RatePerMinuteUsd = 0 });
            var definitions = baseline.ToDictionary(record => record.Number, record => new TableDefinition
                { TableNumber = record.Number, Name = $"Synthetic graphical MT{record.Number}" });
            db.TableDefinitions.AddRange(definitions.Values);
            await db.SaveChangesAsync(deadline.Token);
            var tables = new TableDistributionService(db);
            foreach (var record in baseline.OrderBy(record => record.SortOrder))
                await tables.UpsertVersionAsync(set.Id, definitions[record.Number].Id, record.Number == 92 ? 2 : 1,
                    prepared[record.Number], record.SortOrder, record.Dependency is { } dep ? definitions[dep].Id : null, deadline.Token);
            await tables.PublishTableSetAsync(set.Id, deadline.Token);
            await tables.AssignTableSetAsync(terminal.Id, set.Id, null, null, deadline.Token);
            var saved = await db.TableVersions.Include(row => row.TableDefinition).Include(row => row.TablePayload)
                .Where(row => row.TableSetId == set.Id).ToListAsync(deadline.Token);
            if (saved.Count != Expected.Length || saved.Any(row => !TableDistributionService.GetEffectivePayload(row)
                    .AsSpan().SequenceEqual(prepared[row.TableDefinition!.TableNumber])))
                throw new InvalidOperationException("Isolated graphical set readback mismatch.");
            foreach (var route in new[] { settings.Destination, "1" + settings.Destination })
            {
                var quote = await RatingWorkflow.QuoteAsync(db, new RatingWorkflow.QuoteRequestDto(route, RatingMode.RealTimeRated,
                    plan.Id, customer.Id, 1m, DateTime.UtcNow), deadline.Token);
                Assert.True(quote.Allowed);
                Assert.Equal(0m, quote.AmountUsd);
            }
            terminalId = terminal.Id;
            setId = set.Id;
        }
        using (var probe = new TcpClient())
            await probe.ConnectAsync(IPAddress.Loopback, settings.Port, deadline.Token);
        await WriteJson(settings.ReadyFile, new { listening = true, host = "127.0.0.1", settings.Port,
            settings.TerminalWireId, settings.Destination, syntheticOnly = true, quotedUsd = 0m, setId,
            records = Expected.Length, changedRecordNumbers = new[] { 92 } });

        var completed = false;
        while (!deadline.IsCancellationRequested)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var batches = await db.DownloadBatches.AsNoTracking().Include(row => row.Items)
                .Where(row => row.TerminalId == terminalId).ToListAsync(deadline.Token);
            var quotes = await db.CallRecords.AsNoTracking().Include(row => row.Results)
                .Where(row => row.TerminalId == terminalId).ToListAsync(deadline.Token);
            var uploads = await db.UploadBatches.AsNoTracking().Where(row => row.TerminalId == terminalId).ToListAsync(deadline.Token);
            var sessions = await db.NccSessions.AsNoTracking().Where(row => row.TerminalId == terminalId).ToListAsync(deadline.Token);
            completed |= batches.Any(batch => batch.Status == DownloadBatchStatus.Completed && batch.Items.Count == Expected.Length
                && batch.Items.All(item => item.Succeeded));
            await WriteJson(settings.ResultFile, new
            {
                syntheticOnly = true, settings.Destination, downloaded = completed,
                downloads = batches.Select(batch => new { batch.Status, total = batch.Items.Count,
                    acknowledged = batch.Items.Count(item => item.Succeeded), batch.LastError }).ToArray(),
                quotes = quotes.Select(row => new { row.DialedDigits, amountsUsd = row.Results.Select(result => result.Amount).ToArray() }).ToArray(),
                callDetailUploads = uploads.Count(row => TerminalUploads.IsCallDetail(row.IdempotencyKey)),
                statisticsUploads = uploads.Count(row => TerminalUploads.IsStatistics(row.IdempotencyKey)),
                matchingSessions = sessions.Count,
                activeSessions = sessions.Count(row => row.Status == NccSessionStatus.Active),
                audioServiceBehaviorVerifiedByHost = false
            });
            // A stop signal ends this local fixture. It is not evidence of terminal audio behavior.
            if (File.Exists(settings.StopFile))
                break;
            try { await Task.Delay(1000, deadline.Token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { break; }
        }
        Assert.True(completed, "No complete eighteen-record acknowledgement batch was observed in the isolated graphical lab.");
    }

    private static Dictionary<int, byte[]> PrepareRecords(LabSettings settings, LabRecord[] baseline)
    {
        if (!baseline.OrderBy(record => record.SortOrder).Select(record => record.Number).SequenceEqual(Expected))
            throw new InvalidOperationException("The isolated graphical baseline must contain the exact eighteen-record set.");
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        var records = new Dictionary<int, byte[]>();
        foreach (var record in baseline)
        {
            var payload = Convert.FromBase64String(record.PayloadBase64);
            if (payload.Length != profile.VerifiedLengths[record.Number] || payload[0] != record.Number
                || !Convert.ToHexString(SHA256.HashData(payload)).Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Graphical baseline MT{record.Number} did not match its saved hash or profile.");
            records.Add(record.Number, payload);
        }
        var access = NativeTableLayouts.Find(21, records[21], profile.LayoutFor(21))!;
        if (access.Sections.SelectMany(section => section.Fields).Single(field => field.Key == "terminalNumber").Read(records[21])
            != settings.TerminalWireId)
            throw new InvalidOperationException("The graphical baseline belongs to another wire ID.");
        var screen = NativeTableLayouts.Find(92, records[92], profile.LayoutFor(92))!;
        var fields = screen.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        foreach (var slot in new[] { 3, 4 })
            if (!string.IsNullOrEmpty(fields[$"rule-{slot}.pattern"].Read(records[92])))
                throw new InvalidOperationException("A selected graphical lab screening slot is not empty.");
        var changes = new Dictionary<string, string>();
        foreach (var (slot, pattern) in new[] { (3, settings.Destination), (4, "1" + settings.Destination) })
        {
            var prefix = $"rule-{slot}.";
            changes[prefix + "pattern"] = pattern;
            changes[prefix + "wildcards"] = "0";
            changes[prefix + "classification"] = "3";
            changes[prefix + "payment"] = "2";
            changes[prefix + "rate"] = "255";
            changes[prefix + "free"] = "true";
            changes[prefix + "answer"] = "true";
            changes[prefix + "targetCdr"] = "true";
            foreach (var flag in new[] { "carrier", "fallback", "mute", "preDialDelay", "coinException", "featureGroupB",
                "denyModemData", "denyDataPort", "restrictedAccess", "translated" })
                changes[prefix + flag] = "false";
        }
        Assert.Equal(records[92], screen.Apply(records[92], new Dictionary<string, string>()));
        records[92] = screen.Apply(records[92], changes);
        foreach (var change in changes)
            Assert.Equal(change.Value, fields[change.Key].Read(records[92]));
        var changed = baseline.Where(record => !Convert.ToHexString(SHA256.HashData(records[record.Number]))
            .Equals(record.Sha256, StringComparison.OrdinalIgnoreCase)).Select(record => record.Number).Order().ToArray();
        Assert.Equal(new[] { 92 }, changed);
        return records;
    }

    private static Task WriteJson(string path, object value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json));
    private sealed record LabRecord(int Number, int SortOrder, int? Dependency, string Sha256, string PayloadBase64);
    private sealed record LabSettings(string TerminalWireId, string Destination, int Port, int TimeoutSeconds,
        string ReadyFile, string ResultFile, string TablePackageFile, string StopFile)
    {
        public void Validate()
        {
            var paths = new[] { ReadyFile, ResultFile, TablePackageFile, StopFile };
            if (!Regex.IsMatch(TerminalWireId, "^[0-9]{3}55501[0-9]{2}$") || !Regex.IsMatch(Destination, "^[0-9]{3}55501[0-9]{2}$")
                || Port is < 1024 or > 65535 || TimeoutSeconds is < 30 or > 1800 || paths.Any(string.IsNullOrWhiteSpace)
                || paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length || File.Exists(StopFile))
                throw new InvalidOperationException("Use fictional line IDs and distinct local fixture paths.");
        }
    }

    private sealed class LabFactory(int port) : WebApplicationFactory<Program>
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
        }
    }
}
