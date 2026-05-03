using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Options;
using HostPlatform.Api.Security;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Controllers;

/// <summary>
/// PCI DSS boundary: APIs operate on token references, masked PAN fragments, and opaque JSON payloads — never persist full magnetic tracks,
/// full PAN, or CVV through these endpoints. Production integrations must terminate cardholder data in a PCI zone upstream.
/// </summary>
[ApiController]
[Route("api/cards")]
public sealed class CardsController(
    HostPlatformDbContext db,
    IOptions<CardPaymentOptions> cardOptions) : ControllerBase
{
    private readonly CardPaymentOptions _opts = cardOptions.Value;

    /// <summary>Advertises simulation gates for UI banners — safe without secrets.</summary>
    [HttpGet("simulation-state")]
    public ActionResult<object> SimulationState() =>
        Ok(new
        {
            simulationMode = _opts.SimulationMode,
            physicalCardWritesDisabled = _opts.PhysicalCardWritesDisabled,
            banner =
                "Simulation mode — ledger and reconciliation are lab scaffolding only; physical card writes are disabled until HARDWARE_VALIDATION_REQUIRED."
        });

    [HttpGet("products")]
    public async Task<ActionResult<IEnumerable<object>>> Products(CancellationToken ct) =>
        Ok(await db.CardProducts.AsNoTracking()
            .OrderBy(p => p.Code)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Code,
                defaultCardType = p.DefaultCardType,
                p.AllowNegativeBalance,
                p.IsTestFixtureCatalogEntry
            }).ToListAsync(ct));

    [HttpGet("products/{id:guid}")]
    public async Task<ActionResult<object>> Product(Guid id, CancellationToken ct)
    {
        var p = await db.CardProducts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return p == null
            ? NotFound()
            : Ok(new
            {
                p.Id,
                p.Name,
                p.Code,
                defaultCardType = p.DefaultCardType,
                p.AllowNegativeBalance,
                p.IsTestFixtureCatalogEntry
            });
    }

    [HttpPost("products")]
    public async Task<ActionResult<object>> CreateProduct([FromBody] ProductDto body, CancellationToken ct)
    {
        var p = new CardProduct
        {
            Name = body.Name,
            Code = body.Code,
            DefaultCardType = body.DefaultCardType ?? CardType.Unknown,
            AllowNegativeBalance = body.AllowNegativeBalance ?? false,
            IsTestFixtureCatalogEntry = body.IsTestFixtureCatalogEntry ?? false
        };
        db.CardProducts.Add(p);
        ApiAudit.Write(db, HttpContext, "cards", "create_card_product", $"CardProduct:{p.Id}",
            new { p.Name, p.Code, p.DefaultCardType });
        await db.SaveChangesAsync(ct);
        return Created($"/api/cards/products/{p.Id}", new { p.Id });
    }

    [HttpGet("accounts")]
    public async Task<ActionResult<IEnumerable<object>>> Accounts(CancellationToken ct) =>
        Ok(await db.CardAccounts.AsNoTracking()
            .Select(a => new
            {
                a.Id,
                a.CardProductId,
                a.TerminalId,
                panLast4Display = CardIdentifierRedaction.MaskPanLast4(a.PanLast4),
                credentialTokenMasked = CardIdentifierRedaction.MaskCredentialToken(a.CredentialTokenRef),
                resolvedCardType = a.ResolvedCardType,
                credentialKind = a.CredentialKind,
                a.Balance
            }).ToListAsync(ct));

    [HttpGet("accounts/{id:guid}")]
    public async Task<ActionResult<object>> Account(Guid id, CancellationToken ct)
    {
        var a = await db.CardAccounts.AsNoTracking()
            .Include(x => x.CardProduct)
            .Include(x => x.CardBalance)
            .Include(x => x.SmartcardProfile)
            .Include(x => x.EPurseProfile)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a == null)
            return NotFound();
        return Ok(new
        {
            a.Id,
            a.CardProductId,
            productCode = a.CardProduct!.Code,
            a.TerminalId,
            panLast4Display = CardIdentifierRedaction.MaskPanLast4(a.PanLast4),
            credentialTokenMasked = CardIdentifierRedaction.MaskCredentialToken(a.CredentialTokenRef),
            resolvedCardType = a.ResolvedCardType,
            credentialKind = a.CredentialKind,
            a.Balance,
            cardBalance = a.CardBalance == null
                ? null
                : new { a.CardBalance.Amount, a.CardBalance.Currency, a.CardBalance.UpdatedAtUtc },
            smartcardProfile = a.SmartcardProfile == null
                ? null
                : new { a.SmartcardProfile.ProfileJson, a.SmartcardProfile.SmartcardTypeId },
            epurseProfile = a.EPurseProfile == null ? null : new { a.EPurseProfile.ProfileJson },
            simulationMode = _opts.SimulationMode
        });
    }

    /// <summary>Ledger + ingest events for operator traceability (simulation ledger — not issuer authoritative).</summary>
    [HttpGet("accounts/{id:guid}/timeline")]
    public async Task<ActionResult<object>> AccountTimeline(Guid id, [FromQuery] int take = 40, CancellationToken ct = default)
    {
        var exists = await db.CardAccounts.AsNoTracking().AnyAsync(a => a.Id == id, ct);
        if (!exists)
            return NotFound();

        var n = Math.Clamp(take, 1, 100);
        var adjustments = await db.BalanceAdjustments.AsNoTracking()
            .Where(a => a.CardAccountId == id)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(n)
            .Select(a => new
            {
                kind = "balanceAdjustment",
                atUtc = a.CreatedAtUtc,
                title = $"Balance adjustment {(a.Delta >= 0 ? "+" : "")}{a.Delta}",
                detail = new { a.Id, a.Delta, a.Reason, a.SimulationMode }
            })
            .ToListAsync(ct);

        var payments = await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.CardAccountId == id)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(n)
            .Select(t => new
            {
                kind = "paymentTransaction",
                atUtc = t.CreatedAtUtc,
                title = $"Payment {t.Amount}",
                detail = new
                {
                    t.Id,
                    t.Amount,
                    reconciliation = t.Reconciliation,
                    reportedCardType = t.ReportedCardType,
                    t.DetailJson,
                    rawPayloadJson = t.RawPayloadJson
                }
            })
            .ToListAsync(ct);

        var reads = await db.CardReadEvents.AsNoTracking()
            .Where(e => e.CardAccountId == id)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(n)
            .Select(e => new
            {
                kind = "cardRead",
                atUtc = e.CreatedAtUtc,
                title = "Card read event",
                detail = new { e.Id, reportedCardType = e.ReportedCardType, e.RawPayloadJson, e.TerminalId }
            })
            .ToListAsync(ct);

        var writes = await db.CardWriteEvents.AsNoTracking()
            .Where(e => e.CardAccountId == id)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(n)
            .Select(e => new
            {
                kind = "cardWrite",
                atUtc = e.CreatedAtUtc,
                title = "Card write event",
                detail = new { e.Id, e.RawPayloadJson }
            })
            .ToListAsync(ct);

        var audits = await db.AuditEvents.AsNoTracking()
            .Where(e => e.Resource.Contains(id.ToString()) || e.DetailJson.Contains(id.ToString()))
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(n)
            .Select(e => new
            {
                kind = "audit",
                atUtc = e.CreatedAtUtc,
                title = $"{e.Category}/{e.Action}",
                detail = new { e.Id, e.Category, e.Action, e.Actor, e.Resource, e.DetailJson, e.TerminalId }
            })
            .ToListAsync(ct);

        var merged = adjustments
            .Select(a => (a.atUtc, (object)a))
            .Concat(payments.Select(p => (p.atUtc, (object)p)))
            .Concat(reads.Select(r => (r.atUtc, (object)r)))
            .Concat(writes.Select(w => (w.atUtc, (object)w)))
            .Concat(audits.Select(x => (x.atUtc, (object)x)))
            .OrderByDescending(x => x.Item1)
            .Take(n)
            .Select(x => x.Item2)
            .ToList();

        return Ok(new { cardAccountId = id, items = merged });
    }

    [HttpPost("accounts")]
    public async Task<ActionResult<object>> CreateAccount([FromBody] AccountDto body, CancellationToken ct)
    {
        var product = await db.CardProducts.FirstOrDefaultAsync(p => p.Id == body.CardProductId, ct);
        if (product == null)
            return BadRequest(new { error = "Unknown CardProductId" });

        var resolved = body.ResolvedCardType ?? product.DefaultCardType;
        var a = new CardAccount
        {
            CardProductId = body.CardProductId,
            TerminalId = body.TerminalId,
            PanLast4 = body.PanLast4 ?? "",
            Balance = body.Balance,
            ResolvedCardType = resolved,
            CredentialTokenRef = body.CredentialTokenRef ?? "",
            CredentialKind = body.CredentialKind ?? CardCredentialKind.OpaqueToken
        };
        db.CardAccounts.Add(a);
        await db.SaveChangesAsync(ct);

        db.CardBalances.Add(new CardBalance
        {
            CardAccountId = a.Id,
            Amount = a.Balance,
            Currency = string.IsNullOrWhiteSpace(body.Currency) ? "USD" : body.Currency!
        });

        if (!string.IsNullOrWhiteSpace(body.SupplementalTokenReference))
        {
            db.CardCredentials.Add(new CardCredential
            {
                CardAccountId = a.Id,
                TokenReference = body.SupplementalTokenReference,
                Kind = CardCredentialKind.VaultReference,
                Active = true
            });
        }

        ApiAudit.Write(db, HttpContext, "cards", "create_card_account", $"CardAccount:{a.Id}",
            new { a.CardProductId, resolvedCardType = resolved, credMasked = CardIdentifierRedaction.MaskCredentialToken(a.CredentialTokenRef) });

        await db.SaveChangesAsync(ct);
        return Created($"/api/cards/accounts/{a.Id}", new { a.Id });
    }

    [HttpPost("accounts/{id:guid}/adjust-balance")]
    public async Task<ActionResult<object>> Adjust(Guid id, [FromBody] AdjustDto body, CancellationToken ct)
    {
        if (!body.SimulationMode)
            return Conflict(new { error = "SimulationMode required until HARDWARE_VALIDATION_REQUIRED (ledger placeholder)." });

        var reason = (body.Reason ?? "").Trim();
        if (reason.Length < 3)
            return BadRequest(new { error = "Audit reason required (min 3 characters)." });

        var a = await db.CardAccounts.Include(x => x.CardProduct).Include(x => x.CardBalance)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a == null)
            return NotFound();

        var product = a.CardProduct ?? await db.CardProducts.FirstAsync(p => p.Id == a.CardProductId, ct);
        var next = a.Balance + body.Delta;
        if (next < 0 && !product.AllowNegativeBalance)
            return BadRequest(new { error = "Negative balance not allowed for this card product.", attemptedBalance = next });

        var before = a.Balance;
        a.Balance = next;

        if (a.CardBalance == null)
            db.CardBalances.Add(new CardBalance { CardAccountId = a.Id, Amount = a.Balance, Currency = "USD" });
        else
            a.CardBalance.Amount = a.Balance;

        db.BalanceAdjustments.Add(new BalanceAdjustment
        {
            CardAccountId = id,
            Delta = body.Delta,
            Reason = reason,
            SimulationMode = body.SimulationMode
        });

        ApiAudit.Write(db, HttpContext, "cards", "adjust_balance", $"CardAccount:{id}",
            new { before, after = a.Balance, body.Delta, reason, simulationMode = body.SimulationMode });

        await db.SaveChangesAsync(ct);
        return Ok(new { a.Id, a.Balance, body.SimulationMode });
    }

    /// <summary>Preserves unknown protocol payloads verbatim — do not strip unrecognized keys.</summary>
    [HttpPost("read-events")]
    public async Task<ActionResult<object>> ReadEvent([FromBody] CardReadEventDto body, CancellationToken ct)
    {
        var raw = string.IsNullOrWhiteSpace(body.RawPayloadJson) ? "{}" : body.RawPayloadJson!;
        var ev = new CardReadEvent
        {
            CardAccountId = body.CardAccountId,
            TerminalId = body.TerminalId,
            ReportedCardType = body.ReportedCardType ?? CardType.Unknown,
            RawPayloadJson = raw
        };
        db.CardReadEvents.Add(ev);
        ApiAudit.Write(db, HttpContext, "cards", "card_read_event", $"CardReadEvent:{ev.Id}",
            new { body.CardAccountId, reportedCardType = ev.ReportedCardType, payloadLength = raw.Length });
        await db.SaveChangesAsync(ct);
        return Created($"/api/cards/read-events", new { ev.Id });
    }

    [HttpPost("transactions")]
    public async Task<ActionResult<object>> PostTransaction([FromBody] PaymentTransactionDto body, CancellationToken ct)
    {
        var account = await db.CardAccounts.FirstOrDefaultAsync(a => a.Id == body.CardAccountId, ct);
        if (account == null)
            return BadRequest(new { error = "Unknown CardAccountId" });

        var raw = string.IsNullOrWhiteSpace(body.RawPayloadJson) ? "{}" : body.RawPayloadJson!;
        var tx = new PaymentTransaction
        {
            CardAccountId = body.CardAccountId,
            Amount = body.Amount,
            Reconciliation = body.Reconciliation ?? ReconciliationStatus.Pending,
            DetailJson = body.DetailJson ?? "{}",
            ReportedCardType = body.ReportedCardType ?? account.ResolvedCardType,
            RawPayloadJson = raw
        };
        db.PaymentTransactions.Add(tx);
        ApiAudit.Write(db, HttpContext, "cards", "payment_transaction", $"PaymentTransaction:{tx.Id}",
            new { body.CardAccountId, body.Amount, tx.ReportedCardType });
        await db.SaveChangesAsync(ct);
        return Created($"/api/cards/transactions", new { tx.Id });
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<IEnumerable<object>>> Transactions([FromQuery] Guid? cardAccountId, CancellationToken ct)
    {
        var q = db.PaymentTransactions.AsNoTracking().AsQueryable();
        if (cardAccountId.HasValue)
            q = q.Where(t => t.CardAccountId == cardAccountId.Value);
        return Ok(await q.OrderByDescending(t => t.CreatedAtUtc).Take(500)
            .Select(t => new
            {
                t.Id,
                t.CardAccountId,
                t.Amount,
                reconciliation = t.Reconciliation,
                reportedCardType = t.ReportedCardType,
                t.CreatedAtUtc
            }).ToListAsync(ct));
    }

    /// <summary>Recent reconciliation batches for operator UI — newest first.</summary>
    [HttpGet("reconciliation-batches")]
    public async Task<ActionResult<IEnumerable<object>>> ListReconciliationBatches(CancellationToken ct) =>
        Ok(await db.CardReconciliationBatches.AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .Take(100)
            .Select(b => new
            {
                b.Id,
                status = b.Status,
                b.CreatedAtUtc,
                b.PostedAtUtc,
                b.ClosedAtUtc
            }).ToListAsync(ct));

    [HttpPost("reconciliation-batches")]
    public async Task<ActionResult<object>> PostReconciliationBatch([FromBody] ReconciliationBatchCreateDto body, CancellationToken ct)
    {
        var batch = new CardReconciliationBatch
        {
            Status = CardReconciliationBatchStatus.Open,
            DetailJson = body.DetailJson ?? "{}"
        };
        db.CardReconciliationBatches.Add(batch);
        ApiAudit.Write(db, HttpContext, "cards", "reconciliation_batch_open", $"CardReconciliationBatch:{batch.Id}",
            new { batch.Status });
        await db.SaveChangesAsync(ct);
        return Created($"/api/cards/reconciliation-batches/{batch.Id}", new { batch.Id });
    }

    [HttpGet("reconciliation-batches/{id:guid}")]
    public async Task<ActionResult<object>> GetReconciliationBatch(Guid id, CancellationToken ct)
    {
        var b = await db.CardReconciliationBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return b == null
            ? NotFound()
            : Ok(new
            {
                b.Id,
                status = b.Status,
                b.DetailJson,
                b.PostedAtUtc,
                b.ClosedAtUtc,
                b.CreatedAtUtc
            });
    }

    [HttpPost("reconciliation-batches/{id:guid}/post")]
    public async Task<ActionResult<object>> PostReconciliationPosted(Guid id, CancellationToken ct)
    {
        var b = await db.CardReconciliationBatches.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b == null)
            return NotFound();
        if (b.Status != CardReconciliationBatchStatus.Open)
            return Conflict(new { error = "Batch must be Open to post." });

        b.Status = CardReconciliationBatchStatus.Posted;
        b.PostedAtUtc = DateTime.UtcNow;
        ApiAudit.Write(db, HttpContext, "cards", "reconciliation_batch_posted", $"CardReconciliationBatch:{id}",
            new { from = CardReconciliationBatchStatus.Open, to = b.Status });
        await db.SaveChangesAsync(ct);
        return Ok(new { b.Id, b.Status });
    }

    [HttpPost("reconciliation-batches/{id:guid}/close")]
    public async Task<ActionResult<object>> PostReconciliationClose(Guid id, [FromBody] ReconciliationCloseDto body, CancellationToken ct)
    {
        if (!body.Confirm)
            return BadRequest(new { error = "confirm:true required to close reconciliation batch." });

        var b = await db.CardReconciliationBatches.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b == null)
            return NotFound();
        if (b.Status != CardReconciliationBatchStatus.Posted)
            return Conflict(new { error = "Batch must be Posted before close." });

        b.Status = CardReconciliationBatchStatus.Closed;
        b.ClosedAtUtc = DateTime.UtcNow;
        ApiAudit.Write(db, HttpContext, "cards", "reconciliation_batch_closed", $"CardReconciliationBatch:{id}",
            new { from = CardReconciliationBatchStatus.Posted, to = b.Status });
        await db.SaveChangesAsync(ct);
        return Ok(new { b.Id, b.Status });
    }

    /// <summary>
    /// Marks batch Exception when reconciliation cannot be settled — destructive operator action; detail semantics HARDWARE_VALIDATION_REQUIRED for production settlement engines.
    /// </summary>
    [HttpPost("reconciliation-batches/{id:guid}/exception")]
    public async Task<ActionResult<object>> PostReconciliationException(Guid id, [FromBody] ReconciliationExceptionDto body, CancellationToken ct)
    {
        if (!body.Confirm)
            return BadRequest(new { error = "confirm:true required to mark reconciliation batch as Exception." });

        var b = await db.CardReconciliationBatches.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b == null)
            return NotFound();
        if (b.Status == CardReconciliationBatchStatus.Closed || b.Status == CardReconciliationBatchStatus.Exception)
            return Conflict(new { error = "Batch is already terminal (Closed or Exception)." });

        var prev = b.Status;
        b.Status = CardReconciliationBatchStatus.Exception;
        if (!string.IsNullOrWhiteSpace(body.Note))
            b.DetailJson = JsonSerializer.Serialize(new { exceptionNote = body.Note, priorDetailJson = b.DetailJson ?? "{}" });

        ApiAudit.Write(db, HttpContext, "cards", "reconciliation_batch_exception", $"CardReconciliationBatch:{id}",
            new { from = prev, to = b.Status, hadNote = !string.IsNullOrWhiteSpace(body.Note) });

        await db.SaveChangesAsync(ct);
        return Ok(new { b.Id, b.Status });
    }

    /// <summary>Physical card writes are simulated only — never executes issuer personalization without HARDWARE_VALIDATION_REQUIRED.</summary>
    [HttpPost("write-events")]
    public async Task<ActionResult<object>> WriteEvent([FromBody] CardWriteEventDto body, CancellationToken ct)
    {
        if (_opts.PhysicalCardWritesDisabled && body is { SimulationMode: false })
            return Conflict(new
            {
                error = "PhysicalCardWritesDisabled",
                detail = "Non-simulated writes blocked — HARDWARE_VALIDATION_REQUIRED."
            });

        if (!body.SimulationMode)
            return Conflict(new { error = "Non-simulation writes not implemented (HARDWARE_VALIDATION_REQUIRED)." });

        var ev = new CardWriteEvent
        {
            CardAccountId = body.CardAccountId,
            IntendedOperation = body.IntendedOperation ?? "unspecified",
            RawPayloadJson = string.IsNullOrWhiteSpace(body.RawPayloadJson) ? "{}" : body.RawPayloadJson!,
            Disposition = CardWriteDisposition.Simulated,
            SimulationMode = true
        };
        db.CardWriteEvents.Add(ev);
        ApiAudit.Write(db, HttpContext, "cards", "card_write_simulated", $"CardWriteEvent:{ev.Id}",
            new { disposition = ev.Disposition, intendedOperation = ev.IntendedOperation });
        await db.SaveChangesAsync(ct);
        return Created($"/api/cards/write-events", new { ev.Id, ev.Disposition });
    }

    public sealed record ProductDto(
        string Name,
        string Code,
        CardType? DefaultCardType,
        bool? AllowNegativeBalance,
        bool? IsTestFixtureCatalogEntry);

    public sealed record AccountDto(
        Guid CardProductId,
        Guid? TerminalId,
        string? PanLast4,
        decimal Balance,
        CardType? ResolvedCardType,
        string? CredentialTokenRef,
        CardCredentialKind? CredentialKind,
        string? Currency,
        string? SupplementalTokenReference);

    public sealed record AdjustDto(decimal Delta, string? Reason, bool SimulationMode);

    public sealed record CardReadEventDto(Guid? CardAccountId, Guid? TerminalId, CardType? ReportedCardType, string? RawPayloadJson);

    public sealed record PaymentTransactionDto(
        Guid CardAccountId,
        decimal Amount,
        ReconciliationStatus? Reconciliation,
        string? DetailJson,
        CardType? ReportedCardType,
        string? RawPayloadJson);

    public sealed record ReconciliationBatchCreateDto(string? DetailJson);

    public sealed record ReconciliationCloseDto(bool Confirm);

    public sealed record ReconciliationExceptionDto(bool Confirm, string? Note);

    public sealed record CardWriteEventDto(Guid? CardAccountId, string? IntendedOperation, string? RawPayloadJson, bool SimulationMode);
}
