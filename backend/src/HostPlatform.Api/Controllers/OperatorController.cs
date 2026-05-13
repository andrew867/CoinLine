using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

/// <summary>
/// Operator-console read-only aggregates — search, dashboards, and unified timelines.
/// Does not mutate protocol payloads; merged timelines reference ids only.
/// Uncertainty on modem/NCC/DLOG paths remains flagged via nested <c>HARDWARE_VALIDATION_REQUIRED</c> strings and dashboard notices.
/// </summary>
[ApiController]
[Route("api/operator")]
public sealed class OperatorController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<object>> Dashboard(CancellationToken ct)
    {
        var terminalsByStatus = await db.Terminals.AsNoTracking()
            .GroupBy(t => t.Status)
            .Select(g => new { status = (int)g.Key, count = g.Count() })
            .ToListAsync(ct);

        var activeCraftSessions = await db.CraftSessions.AsNoTracking()
            .CountAsync(s => s.EndedAtUtc == null, ct);

        var failedDownloads = await db.DownloadBatches.AsNoTracking()
            .CountAsync(d => d.Status == DownloadBatchStatus.Failed, ct);

        var failedUploads = await db.UploadBatches.AsNoTracking()
            .CountAsync(u => u.Status == UploadBatchStatus.Failed, ct);

        var quarantinedUploads = await db.UploadBatches.AsNoTracking()
            .CountAsync(u => u.Status == UploadBatchStatus.Quarantined, ct);

        var recentUploads = await db.UploadBatches.AsNoTracking()
            .OrderByDescending(u => u.CreatedAtUtc)
            .Take(8)
            .Select(u => new
            {
                u.Id,
                u.TerminalId,
                status = (int)u.Status,
                u.CreatedAtUtc,
                u.UpdatedAtUtc,
                previewBytes = u.RawPayload.Length
            })
            .ToListAsync(ct);

        var recentFirmwareJobs = await db.FirmwareUpdateJobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(10)
            .Select(j => new
            {
                j.Id,
                j.TerminalId,
                j.Status,
                j.SimulationMode,
                j.CreatedAtUtc,
                j.FirmwarePackageId
            })
            .ToListAsync(ct);

        var recentAudit = await db.AuditEvents.AsNoTracking()
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(15)
            .Select(e => new
            {
                e.Id,
                e.Category,
                e.Action,
                e.Actor,
                e.Resource,
                e.TerminalId,
                e.CreatedAtUtc
            })
            .ToListAsync(ct);

        var openNccSessions = await db.NccSessions.AsNoTracking()
            .CountAsync(s => s.Status == NccSessionStatus.Active, ct);

        return Ok(new
        {
            terminalsByStatus,
            activeCraftSessions,
            openNccSessions,
            failedDownloads,
            uploadAlerts = new { failed = failedUploads, quarantined = quarantinedUploads },
            recentUploadBatches = recentUploads,
            recentFirmwareJobs,
            recentAuditEvents = recentAudit,
            hardwareValidationNotice =
                "Dashboard aggregates are host-side only — modem/NCC/DLOG paths remain HARDWARE_VALIDATION_REQUIRED until certified."
        });
    }

    [HttpGet("search")]
    public async Task<ActionResult<object>> Search([FromQuery] string? q, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit, 1, 50);
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(new
            {
                query = "",
                customers = Array.Empty<object>(),
                sites = Array.Empty<object>(),
                terminals = Array.Empty<object>(),
                cardAccounts = Array.Empty<object>()
            });
        }

        var term = q.Trim();
        var lower = term.ToLowerInvariant();

        if (Guid.TryParse(term, out var gid))
        {
            var cOne = await db.Customers.AsNoTracking()
                .Where(x => x.Id == gid)
                .Select(x => new { kind = "customer", x.Id, x.Name, x.Code })
                .ToListAsync(ct);
            var sOne = await db.Sites.AsNoTracking()
                .Where(x => x.Id == gid)
                .Select(x => new { kind = "site", x.Id, x.Name, x.Code, x.CustomerId })
                .ToListAsync(ct);
            var tOne = await db.Terminals.AsNoTracking()
                .Where(x => x.Id == gid)
                .Select(x => new { kind = "terminal", x.Id, x.DisplayName, x.TerminalIdHex, x.SiteId })
                .ToListAsync(ct);
            var aOne = await db.CardAccounts.AsNoTracking()
                .Where(x => x.Id == gid)
                .Select(x => new { kind = "cardAccount", x.Id, x.CardProductId, x.TerminalId })
                .ToListAsync(ct);

            return Ok(new
            {
                query = term,
                customers = cOne,
                sites = sOne,
                terminals = tOne,
                cardAccounts = aOne
            });
        }

        var customers = await db.Customers.AsNoTracking()
            .Where(x => x.Name.ToLower().Contains(lower) || x.Code.ToLower().Contains(lower))
            .OrderBy(x => x.Name)
            .Take(take)
            .Select(x => new { kind = "customer", x.Id, x.Name, x.Code })
            .ToListAsync(ct);

        var sites = await db.Sites.AsNoTracking()
            .Where(x => x.Name.ToLower().Contains(lower) || x.Code.ToLower().Contains(lower))
            .OrderBy(x => x.Name)
            .Take(take)
            .Select(x => new { kind = "site", x.Id, x.Name, x.Code, x.CustomerId })
            .ToListAsync(ct);

        var terminals = await db.Terminals.AsNoTracking()
            .Where(x =>
                x.DisplayName.ToLower().Contains(lower) ||
                x.TerminalIdHex.ToLower().Contains(lower.Replace(" ", "")))
            .OrderBy(x => x.DisplayName)
            .Take(take)
            .Select(x => new { kind = "terminal", x.Id, x.DisplayName, x.TerminalIdHex, x.SiteId })
            .ToListAsync(ct);

        var cardAccounts = await db.CardAccounts.AsNoTracking()
            .Where(x =>
                x.PanLast4.ToLower().Contains(lower) ||
                x.CredentialTokenRef.ToLower().Contains(lower))
            .OrderBy(x => x.CreatedAtUtc)
            .Take(take)
            .Select(x => new { kind = "cardAccount", x.Id, x.CardProductId, x.TerminalId })
            .ToListAsync(ct);

        return Ok(new
        {
            query = term,
            customers,
            sites,
            terminals,
            cardAccounts
        });
    }

    [HttpGet("customers/{id:guid}/console")]
    public async Task<ActionResult<object>> CustomerConsole(Guid id, CancellationToken ct)
    {
        var cust = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (cust == null)
            return NotFound();

        var sites = await db.Sites.AsNoTracking()
            .Where(s => s.CustomerId == id)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Code, s.CreatedAtUtc })
            .ToListAsync(ct);

        var siteIds = sites.Select(s => s.Id).ToList();
        var terminals = await db.Terminals.AsNoTracking()
            .Where(t => siteIds.Contains(t.SiteId))
            .OrderBy(t => t.DisplayName)
            .Select(t => new
            {
                t.Id,
                t.SiteId,
                t.DisplayName,
                t.TerminalIdHex,
                status = (int)t.Status
            })
            .ToListAsync(ct);

        var tableSets = await db.TableSets.AsNoTracking()
            .Where(ts => ts.CustomerId == id)
            .OrderBy(ts => ts.Name)
            .Select(ts => new
            {
                ts.Id,
                ts.Name,
                status = (int)ts.Status,
                ts.PublishedAtUtc,
                ts.PublishGeneration
            })
            .ToListAsync(ct);

        var ratePlans = await db.RatePlans.AsNoTracking()
            .Where(r => r.CustomerId == id)
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                mode = (int)r.Mode,
                r.PublishedVersionId
            })
            .ToListAsync(ct);

        var termIds = terminals.Select(t => t.Id).ToHashSet();
        var productsUsed = await db.CardAccounts.AsNoTracking()
            .Where(a => a.TerminalId != null && termIds.Contains(a.TerminalId.Value))
            .Select(a => a.CardProductId)
            .Distinct()
            .ToListAsync(ct);

        var cardProducts = await db.CardProducts.AsNoTracking()
            .Where(p => productsUsed.Contains(p.Id))
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Code, defaultCardType = (int)p.DefaultCardType })
            .ToListAsync(ct);

        return Ok(new
        {
            customer = new { cust.Id, cust.Name, cust.Code, cust.CreatedAtUtc },
            sites,
            terminals,
            tableSets,
            ratePlans,
            cardProducts,
            cardProductsNote =
                cardProducts.Count == 0
                    ? "No card accounts linked to this customer’s terminals yet — catalog is global; link accounts to terminals under this customer."
                    : null
        });
    }

    [HttpGet("customers/{id:guid}/timeline")]
    public async Task<ActionResult<object>> CustomerTimeline(Guid id, [FromQuery] int take = 40, CancellationToken ct = default)
    {
        var exists = await db.Customers.AsNoTracking().AnyAsync(c => c.Id == id, ct);
        if (!exists)
            return NotFound();

        var n = Math.Clamp(take, 1, 100);
        var termIds = await db.Terminals.AsNoTracking()
            .Where(t => db.Sites.Any(s => s.Id == t.SiteId && s.CustomerId == id))
            .Select(t => t.Id)
            .ToListAsync(ct);

        var audits = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TerminalId != null && termIds.Contains(e.TerminalId.Value))
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(n)
            .Select(e => new TimelineItem(
                "audit",
                e.CreatedAtUtc,
                $"{e.Category}/{e.Action}",
                e.Resource,
                new { e.Id, e.Category, e.Action, e.Actor, e.TerminalId, detailPreview = Truncate(e.DetailJson, 200) }))
            .ToListAsync(ct);

        var siteEvents = await db.Sites.AsNoTracking()
            .Where(s => s.CustomerId == id)
            .OrderByDescending(s => s.UpdatedAtUtc)
            .Take(15)
            .Select(s => new TimelineItem(
                "site",
                s.UpdatedAtUtc,
                $"Site {s.Name}",
                s.Code,
                new { s.Id, s.Name, s.Code }))
            .ToListAsync(ct);

        var merged = audits.Concat(siteEvents)
            .OrderByDescending(x => x.AtUtc)
            .Take(n)
            .ToList();

        return Ok(new { customerId = id, items = merged });
    }

    [HttpGet("terminals/{id:guid}/timeline")]
    public async Task<ActionResult<object>> TerminalTimeline(Guid id, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var exists = await db.Terminals.AsNoTracking().AnyAsync(t => t.Id == id, ct);
        if (!exists)
            return NotFound();

        var n = Math.Clamp(take, 1, 150);
        var items = new List<TimelineItem>();

        var events = await db.TerminalEvents.AsNoTracking()
            .Where(e => e.TerminalId == id)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(20)
            .Select(e => new TimelineItem(
                "terminalEvent",
                e.OccurredAtUtc,
                e.EventType,
                e.Id.ToString(),
                new { e.Id, e.EventType, e.PayloadJson }))
            .ToListAsync(ct);
        items.AddRange(events);

        var dlogs = await db.DlogTransactions.AsNoTracking()
            .Where(t => t.TerminalId == id)
            .OrderByDescending(t => t.CapturedAtUtc)
            .Take(15)
            .Select(t => new TimelineItem(
                "dlog",
                t.CapturedAtUtc,
                $"DLOG mt={t.MessageType} {t.MessageTypeName}",
                t.Id.ToString(),
                new
                {
                    t.Id,
                    t.MessageType,
                    t.MessageTypeName,
                    t.IsUnknownMessageType,
                    direction = t.Direction,
                    t.ProcessingStatus
                }))
            .ToListAsync(ct);
        items.AddRange(dlogs);

        var downloads = await db.DownloadBatches.AsNoTracking()
            .Where(b => b.TerminalId == id)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Take(12)
            .Select(b => new TimelineItem(
                "downloadBatch",
                b.CreatedAtUtc,
                $"Download batch — {b.Status}",
                b.Id.ToString(),
                new { b.Id, status = (int)b.Status, b.Scope, b.CompletedAtUtc, b.LastError }))
            .ToListAsync(ct);
        items.AddRange(downloads);

        var uploads = await db.UploadBatches.AsNoTracking()
            .Where(u => u.TerminalId == id)
            .OrderByDescending(u => u.CreatedAtUtc)
            .Take(12)
            .Select(u => new TimelineItem(
                "uploadBatch",
                u.CreatedAtUtc,
                $"Upload batch — {u.Status}",
                u.Id.ToString(),
                new { u.Id, status = (int)u.Status, u.UpdatedAtUtc, rawBytes = u.RawPayload.Length }))
            .ToListAsync(ct);
        items.AddRange(uploads);

        var craft = await db.CraftSessions.AsNoTracking()
            .Where(s => s.TerminalId == id)
            .OrderByDescending(s => s.StartedAtUtc)
            .Take(10)
            .Select(s => new TimelineItem(
                "craftSession",
                s.StartedAtUtc,
                $"Craft session ({(s.EndedAtUtc == null ? "active" : "ended")})",
                s.Id.ToString(),
                new { s.Id, s.TechnicianId, s.OperatorId, s.EndedAtUtc }))
            .ToListAsync(ct);
        items.AddRange(craft);

        var firmware = await db.FirmwareUpdateJobs.AsNoTracking()
            .Where(j => j.TerminalId == id)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(10)
            .Select(j => new TimelineItem(
                "firmwareJob",
                j.CreatedAtUtc,
                $"Firmware job — {j.Status}",
                j.Id.ToString(),
                new { j.Id, status = (int)j.Status, j.SimulationMode, j.FirmwarePackageId }))
            .ToListAsync(ct);
        items.AddRange(firmware);

        var audits = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TerminalId == id)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(25)
            .Select(e => new TimelineItem(
                "audit",
                e.CreatedAtUtc,
                $"{e.Category}/{e.Action}",
                e.Resource,
                new { e.Id, e.Category, e.Action, e.Actor, detailPreview = Truncate(e.DetailJson, 200) }))
            .ToListAsync(ct);
        items.AddRange(audits);

        var ordered = items
            .OrderByDescending(x => x.AtUtc)
            .Take(n)
            .ToList();

        return Ok(new { terminalId = id, items = ordered });
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    private sealed record TimelineItem(string Kind, DateTime AtUtc, string Title, string RefKey, object Detail);
}
