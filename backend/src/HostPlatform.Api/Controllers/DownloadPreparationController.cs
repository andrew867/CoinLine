using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

/// <summary>Prepare the existing, individually assigned data configurations for a reviewed phone list.</summary>
[ApiController]
[Route("api/downloads/preparation")]
public sealed class DownloadPreparationController(HostPlatformDbContext db, TableDistributionService tables, TimeProvider clock) : ControllerBase
{
    private const int MaximumPhones = 40;
    public sealed record PreparationRequest(Guid[] TerminalIds, DateTimeOffset? AvailableAtUtc = null, string? PreviewToken = null);
    private sealed record RecordReview(int TableNumber, Guid VersionId, string PayloadSha256Hex);
    private sealed record PhoneReview(Guid TerminalId, string? TerminalIdHex, string? DisplayName, Guid? TableSetId,
        string? TableSetName, int? AssignmentVersion, int? SetVersion, RecordReview[] Records, string? Reason)
    {
        public bool Eligible => Reason == null;
    }
    private sealed record PreparedPhone(PhoneReview Review, IReadOnlyList<TableVersion> Versions);

    [HttpGet("options")]
    public async Task<IActionResult> Options([FromQuery] string? query, CancellationToken ct)
    {
        var search = query?.Trim() ?? "";
        if (search.Length > 100) return BadRequest(new { error = "Search text must be at most 100 characters." });
        var phones = db.Terminals.AsNoTracking();
        if (search.Length > 0)
        {
            var normalized = search.ToLowerInvariant();
            phones = phones.Where(phone => phone.TerminalIdHex.ToLower().Contains(normalized) || phone.DisplayName.ToLower().Contains(normalized));
        }
        var results = await phones.OrderBy(phone => phone.TerminalIdHex).ThenBy(phone => phone.Id).Take(101)
            .Select(phone => new { id = phone.Id, phone.TerminalIdHex, phone.DisplayName, active = phone.DeactivatedAtUtc == null })
            .ToListAsync(ct);
        return Ok(new { phones = results.Take(100), more = results.Count > 100, maximumPhones = MaximumPhones });
    }

    [HttpPost("preview")]
    public Task<IActionResult> Preview(PreparationRequest body, CancellationToken ct) => ExecuteAsync(body, false, ct);

    [HttpPost]
    public Task<IActionResult> Prepare(PreparationRequest body, CancellationToken ct) => ExecuteAsync(body, true, ct);

    private async Task<IActionResult> ExecuteAsync(PreparationRequest body, bool prepare, CancellationToken ct)
    {
        if (body.TerminalIds is not { Length: >= 1 and <= MaximumPhones } || body.TerminalIds.Contains(Guid.Empty)
            || body.TerminalIds.Distinct().Count() != body.TerminalIds.Length)
            return BadRequest(new { error = $"Choose between 1 and {MaximumPhones} distinct phones." });
        if (prepare && string.IsNullOrWhiteSpace(body.PreviewToken))
            return BadRequest(new { error = "Review the phone list before preparing downloads." });
        var now = clock.GetUtcNow().UtcDateTime;
        var requestedTime = body.AvailableAtUtc?.UtcDateTime;
        if (requestedTime is { } future && future <= now)
            return BadRequest(new { error = "Choose a future preparation time or Prepare now." });
        if (requestedTime is { } exact) requestedTime = exact.AddTicks(-(exact.Ticks % 10));
        var ready = requestedTime ?? now.AddTicks(-(now.Ticks % 10));

        try
        {
            // A concurrent assignment or schedule must not silently replace an operator's review.
            await using var transaction = prepare && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var plan = await ReadPlanAsync(body.TerminalIds, ready, ct);
            var reviews = plan.Select(phone => phone.Review).ToArray();
            var token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { requestedTime, reviews })));
            if (!prepare)
                return Ok(new { phones = reviews, preparationAtUtc = requestedTime, previewToken = token, eligibleCount = reviews.Count(phone => phone.Eligible) });
            if (!string.Equals(token, body.PreviewToken, StringComparison.Ordinal))
                return Conflict(new { error = "The phone list, assignment or existing work changed. Review the downloads again." });
            if (reviews.Any(phone => !phone.Eligible))
                return BadRequest(new { error = "Remove blocked phones or resolve their reported issues before preparing the list." });

            var results = new List<object>();
            foreach (var phone in plan)
            {
                var schedule = new ScheduledTableDownload
                {
                    TerminalId = phone.Review.TerminalId, TableSetId = phone.Review.TableSetId!.Value, AvailableAtUtc = ready
                };
                db.ScheduledTableDownloads.Add(schedule);
                var batch = requestedTime == null ? tables.StageScheduledBatch(schedule, phone.Versions, now) : null;
                ApiAudit.Write(db, HttpContext, "tables.download", "prepare_assigned", schedule.Id.ToString(), new
                {
                    scheduleId = schedule.Id, phone.Review.TableSetId, batchId = batch?.Id, availableAtUtc = ready,
                    records = phone.Review.Records.Select(record => record.TableNumber)
                }, schedule.TerminalId);
                results.Add(new
                {
                    scheduleId = schedule.Id, batchId = batch?.Id, phone.Review.TerminalId, phone.Review.DisplayName,
                    phone.Review.TerminalIdHex, phone.Review.TableSetId, phone.Review.TableSetName, availableAtUtc = ready,
                    preparedAtUtc = schedule.PreparedAtUtc
                });
            }
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Ok(new { downloads = results });
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "An assignment or download changed while preparing this list. Review it again." });
        }
    }

    private async Task<List<PreparedPhone>> ReadPlanAsync(Guid[] ids, DateTime ready, CancellationToken ct)
    {
        var phones = await db.Terminals.AsNoTracking().Where(phone => ids.Contains(phone.Id)).ToDictionaryAsync(phone => phone.Id, ct);
        var assignments = await db.TerminalTableAssignments.AsNoTracking().Include(a => a.TableSet)
            .Where(a => ids.Contains(a.TerminalId)).ToDictionaryAsync(a => a.TerminalId, ct);
        var busy = await db.ScheduledTableDownloads.AsNoTracking().Where(row => ids.Contains(row.TerminalId)
            && row.CompletedAtUtc == null && row.CancelledAtUtc == null).Select(row => row.TerminalId).ToHashSetAsync(ct);
        busy.UnionWith(await db.DownloadBatches.AsNoTracking().Where(row => ids.Contains(row.TerminalId)
            && (row.Status == DownloadBatchStatus.Pending || row.Status == DownloadBatchStatus.Running || row.Status == DownloadBatchStatus.Preparing))
            .Select(row => row.TerminalId).ToListAsync(ct));

        var plan = new List<PreparedPhone>(ids.Length);
        foreach (var id in ids)
        {
            phones.TryGetValue(id, out var phone);
            assignments.TryGetValue(id, out var assignment);
            var set = assignment?.TableSet;
            var reason = phone == null ? "Phone no longer exists."
                : phone.DeactivatedAtUtc != null ? "Phone is deactivated."
                : busy.Contains(id) ? "Phone already has a pending or active download."
                : set == null ? "Phone has no assigned configuration."
                : set.Status != TableSetStatus.Published ? "The assigned configuration is not published." : null;
            IReadOnlyList<TableVersion> records = [];
            if (reason == null)
            {
                try
                {
                    records = await tables.ReadScheduledRecordsAsync(new ScheduledTableDownload
                        { TerminalId = id, TableSetId = set!.Id, AvailableAtUtc = ready }, phone!, ready, ct);
                    if (records.Any(record => record.TableDefinition!.TableNumber == CodeDownloadWire.MessageType))
                        reason = "Use the firmware scheduler for MT94.";
                    else if (records.Any(record => !record.ValidationPassed))
                        reason = "The assigned configuration contains an invalid record.";
                }
                catch (InvalidOperationException ex) { reason = ex.Message; }
                catch (ArgumentException) { reason = "The assigned configuration has conflicting table definitions."; }
            }
            var recordReviews = records.Select(record => new RecordReview(record.TableDefinition!.TableNumber, record.Id, record.PayloadSha256Hex)).ToArray();
            plan.Add(new(new(id, phone?.TerminalIdHex, phone?.DisplayName, set?.Id, set?.Name, assignment?.Version, set?.Version,
                recordReviews, reason), records));
        }
        return plan;
    }
}
