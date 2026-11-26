using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Hosting;
using HostPlatform.Api.Options;
using HostPlatform.Api.Security;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Cards;
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
    IOptions<CardPaymentOptions> cardOptions,
    ICardAccountLedger ledger) : ControllerBase
{
    private readonly CardPaymentOptions _opts = cardOptions.Value;

    /// <summary>Reports physical card-write policy and runtime authorization provider registration.</summary>
    [HttpGet("simulation-state")]
    public ActionResult<object> SimulationState([FromServices] IServiceProviderIsService registrations) =>
        Ok(new
        {
            simulationMode = false,
            hostLedgerEnabled = true,
            runtimeAuthorizationProviderRegistered = registrations.IsService(typeof(INccCardAuthorizationProvider)),
            labAuthorizationEnabled = _opts.LabAuthorization?.Enabled == true,
            labSettlementEnabled = _opts.LabAuthorization?.Enabled == true && _opts.LabAuthorization.SettleUploadedCalls,
            physicalCardWritesDisabled = _opts.PhysicalCardWritesDisabled,
            banner =
                "The host ledger records card payments and call charges. Writing a physical card stays off."
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
                    t.DetailJson
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
                detail = new { e.Id, reportedCardType = e.ReportedCardType, panLast4Display = CardIdentifierRedaction.MaskPanLast4(e.PanLast4), e.Result, e.TerminalId }
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
                detail = new { e.Id, reportedCardType = e.ReportedCardType, panLast4Display = CardIdentifierRedaction.MaskPanLast4(e.PanLast4), e.Disposition }
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

        if (!string.IsNullOrEmpty(body.PanLast4) && !CardIdentifierRedaction.IsLast4(body.PanLast4))
            return BadRequest(new { error = "Supply only the last four ASCII digits, or leave the field blank." });
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
        var reason = (body.Reason ?? "").Trim();
        if (reason.Length < 3)
            return BadRequest(new { error = "Audit reason required (min 3 characters)." });

        var a = await db.CardAccounts.Include(x => x.CardProduct).Include(x => x.CardBalance)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a == null)
            return NotFound();

        var product = a.CardProduct ?? await db.CardProducts.FirstAsync(p => p.Id == a.CardProductId, ct);
        if (a.CardBalance == null)
        {
            var cb = new CardBalance { CardAccountId = a.Id, Amount = a.Balance, Currency = "USD" };
            db.CardBalances.Add(cb);
            a.CardBalance = cb;
        }

        var before = a.Balance;
        try
        {
            ledger.ApplyBalanceAdjustment(a, product, body.Delta);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        db.BalanceAdjustments.Add(new BalanceAdjustment
        {
            CardAccountId = id,
            Delta = body.Delta,
            Reason = reason,
            SimulationMode = false
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
        if (!ValidObservation(body.PanLast4, body.ReportedCardType) ||
            (body.Result is { } result && !Enum.IsDefined(result)))
            return BadRequest(new { error = "Supply a valid card type, read result and optional last four ASCII digits." });
        var account = await FindCardObservationAccountAsync(body.CardAccountId, ct);
        if (body.CardAccountId != null && account == null)
            return BadRequest(new { error = "Unknown CardAccountId" });
        var ev = new CardReadEvent
        {
            CardAccountId = body.CardAccountId,
            TerminalId = body.TerminalId,
            ReportedCardType = body.ReportedCardType ?? account?.ResolvedCardType ?? CardType.Unknown,
            PanLast4 = ObservationLast4(body.PanLast4, account),
            Result = body.Result ?? CardReadResult.Unknown
        };
        db.CardReadEvents.Add(ev);
        ApiAudit.Write(db, HttpContext, "cards", "card_read_event", $"CardReadEvent:{ev.Id}",
            new { body.CardAccountId, reportedCardType = ev.ReportedCardType, ev.Result });
        await db.SaveChangesAsync(ct);
        return Created($"/api/cards/read-events", new { ev.Id });
    }

    [HttpPost("transactions")]
    public async Task<ActionResult<object>> PostTransaction([FromBody] PaymentTransactionDto body, CancellationToken ct)
    {
        var account = await db.CardAccounts.Include(a => a.CardProduct).Include(a => a.CardBalance)
            .FirstOrDefaultAsync(a => a.Id == body.CardAccountId, ct);
        if (account == null)
            return BadRequest(new { error = "Unknown CardAccountId" });

        var product = account.CardProduct ?? await db.CardProducts.FirstAsync(p => p.Id == account.CardProductId, ct);

        if (account.CardBalance == null)
        {
            var cb = new CardBalance { CardAccountId = account.Id, Amount = account.Balance, Currency = "USD" };
            db.CardBalances.Add(cb);
            account.CardBalance = cb;
        }

        try
        {
            ledger.ApplyPaymentAmount(account, product, body.Amount);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var tx = new PaymentTransaction
        {
            CardAccountId = body.CardAccountId,
            Amount = body.Amount,
            Reconciliation = body.Reconciliation ?? ReconciliationStatus.Pending,
            DetailJson = "{}",
            ReportedCardType = body.ReportedCardType ?? account.ResolvedCardType,
            RawPayloadJson = "{}"
        };
        db.PaymentTransactions.Add(tx);

        ApiAudit.Write(db, HttpContext, "cards", "payment_transaction", $"PaymentTransaction:{tx.Id}",
            new { body.CardAccountId, body.Amount, tx.ReportedCardType, balanceApplied = true });
        await db.SaveChangesAsync(ct);
        return Created($"/api/cards/transactions", new { tx.Id, appliedToBalance = true });
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<IEnumerable<object>>> Transactions([FromQuery] Guid? cardAccountId, CancellationToken ct)
    {
        var q = db.PaymentTransactions.AsNoTracking().AsQueryable();
        if (cardAccountId.HasValue)
            q = q.Where(t => t.CardAccountId == cardAccountId.Value);
        var rows = await q.OrderByDescending(t => t.CreatedAtUtc).Take(500)
            .Select(t => new
            {
                t.Id, t.CardAccountId, t.Amount, reconciliation = t.Reconciliation,
                reportedCardType = t.ReportedCardType, t.CreatedAtUtc
            }).ToListAsync(ct);
        var resources = rows.Select(row => row.Id.ToString()).ToArray();
        var synthetic = (await db.AuditEvents.AsNoTracking()
            .Where(row => row.Category == "cards" && row.Action == "synthetic_cdr_settlement" && resources.Contains(row.Resource))
            .Select(row => row.Resource).ToListAsync(ct)).ToHashSet();
        return Ok(rows.Select(row => new
        {
            row.Id, row.CardAccountId, row.Amount, row.reconciliation, row.reportedCardType, row.CreatedAtUtc,
            source = synthetic.Contains(row.Id.ToString()) ? "synthetic_cdr" : "unclassified"
        }));
    }

    /// <summary>Redacted authorization observations and decisions, not settled payments.</summary>
    [HttpGet("authorization-attempts")]
    public async Task<ActionResult<IEnumerable<object>>> AuthorizationAttempts([FromQuery] Guid? terminalId, CancellationToken ct)
    {
        var query = db.AuditEvents.AsNoTracking().Where(row => row.Category == "ncc");
        if (terminalId.HasValue)
            query = query.Where(row => row.TerminalId == terminalId.Value);
        var decisions = await query.Where(row => row.Action == CardAuthorizationAudit.DecisionAction)
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).Take(500).ToListAsync(ct);
        var attempts = decisions.Select(row => (Row: row, Detail: CardAuthorizationAudit.Read(row.DetailJson)))
            .Where(entry => entry.Detail != null).Select(entry => (entry.Row, Detail: entry.Detail!)).ToList();

        // Older listeners preserved a redacted request observation but not its
        // outcome. Represent that as unknown, rather than inventing a decline.
        var observations = await query.Where(row => row.Action == "frame_stored" &&
            (row.DetailJson.Contains("\"redactedMessageType\":1}") || row.DetailJson.Contains("\"redactedMessageType\":2}") ||
             row.DetailJson.Contains("\"redactedMessageType\":1,") || row.DetailJson.Contains("\"redactedMessageType\":2,")))
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).Take(500).ToListAsync(ct);
        var sessionKeys = observations.Select(row => row.TerminalSessionId).OfType<string>().Distinct().ToArray();
        var known = await query.Where(row => row.TerminalSessionId != null && sessionKeys.Contains(row.TerminalSessionId)
            && (row.Action == CardAuthorizationAudit.DecisionAction || row.Action == CardAuthorizationAudit.ReplayAction))
            .Select(row => new { row.TerminalSessionId, row.DetailJson }).ToListAsync(ct);
        var recorded = known.Select(row => (row.TerminalSessionId, Detail: CardAuthorizationAudit.Read(row.DetailJson)))
            .Where(row => row.Detail != null).Select(row => (row.TerminalSessionId, row.Detail!.MessageType)).ToHashSet();
        foreach (var observation in observations)
        {
            if (CardAuthorizationAudit.RedactedRequestType(observation.DetailJson) is not { } type)
                continue;
            if (observation.TerminalSessionId != null && !recorded.Add((observation.TerminalSessionId, type)))
                continue;
            attempts.Add((observation, new CardAuthorizationAuditDetail(type, "unknown", "unrecorded")));
        }

        var latest = attempts.OrderByDescending(entry => entry.Row.CreatedAtUtc).ThenByDescending(entry => entry.Row.Id).Take(500).ToArray();
        var ids = latest.Select(entry => entry.Row.TerminalId).OfType<Guid>().Distinct().ToArray();
        var phones = await db.Terminals.AsNoTracking().Where(row => ids.Contains(row.Id))
            .Select(row => new { row.Id, row.DisplayName }).ToDictionaryAsync(row => row.Id, row => row.DisplayName, ct);
        return Ok(latest.Select(entry => new
        {
            entry.Row.Id, entry.Row.TerminalId,
            terminalDisplayName = entry.Row.TerminalId is { } id ? phones.GetValueOrDefault(id) : null,
            nccSessionId = Guid.TryParse(entry.Row.TerminalSessionId, out var parsed) ? (Guid?)parsed : null,
            entry.Row.CreatedAtUtc, entry.Detail.MessageType, entry.Detail.Outcome, entry.Detail.Provider,
            entry.Detail.Reason, entry.Detail.CardAccountId, entry.Detail.QuoteId, entry.Detail.QuotedAmountUsd, entry.Detail.CallSequence,
            entry.Detail.RequestBytes, entry.Detail.BundledCallRecords
        }));
    }

    [HttpGet("reconciliation-pending")]
    public async Task<ActionResult<object>> ReconciliationPending(CancellationToken ct)
    {
        var paymentAmounts = await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.Reconciliation == ReconciliationStatus.Unknown || t.Reconciliation == ReconciliationStatus.Pending)
            .Select(t => t.Amount)
            .ToListAsync(ct);
        var callCount = await db.CallRecords.AsNoTracking()
            .CountAsync(c => c.Reconciliation == ReconciliationStatus.Unknown || c.Reconciliation == ReconciliationStatus.Pending, ct);
        return Ok(new
        {
            paymentCount = paymentAmounts.Count,
            paymentTotal = paymentAmounts.Sum(),
            callCount
        });
    }

    /// <summary>Recent reconciliation batches for operator UI — newest first.</summary>
    [HttpGet("reconciliation-batches")]
    public async Task<ActionResult<IEnumerable<object>>> ListReconciliationBatches(CancellationToken ct)
    {
        var rows = await db.CardReconciliationBatches.AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);
        return Ok(rows.Select(b =>
        {
            var summary = ReadBatchSummary(b.DetailJson);
            return new
            {
                b.Id,
                status = b.Status,
                b.CreatedAtUtc,
                b.PostedAtUtc,
                b.ClosedAtUtc,
                summary.PaymentCount,
                summary.CallCount,
                summary.PaymentTotal,
                summary.CallTotal
            };
        }));
    }

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
        if (b == null)
            return NotFound();
        var summary = ReadBatchSummary(b.DetailJson);
        return Ok(new
        {
            b.Id,
            status = b.Status,
            b.DetailJson,
            b.PostedAtUtc,
            b.ClosedAtUtc,
            b.CreatedAtUtc,
            summary.PaymentCount,
            summary.CallCount,
            summary.PaymentTotal,
            summary.CallTotal
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

        var payments = await db.PaymentTransactions
            .Where(t => t.Reconciliation == ReconciliationStatus.Unknown || t.Reconciliation == ReconciliationStatus.Pending)
            .ToListAsync(ct);
        var calls = await db.CallRecords
            .Where(c => c.Reconciliation == ReconciliationStatus.Unknown || c.Reconciliation == ReconciliationStatus.Pending)
            .ToListAsync(ct);
        foreach (var payment in payments)
            payment.Reconciliation = ReconciliationStatus.Matched;
        foreach (var call in calls)
            call.Reconciliation = ReconciliationStatus.Matched;

        var callIds = calls.Select(c => c.Id).ToList();
        var callTotal = callIds.Count == 0
            ? 0m
            : await db.RatingResults.Where(r => callIds.Contains(r.CallRecordId)).SumAsync(r => r.Amount, ct);
        var paymentTotal = payments.Sum(p => p.Amount);

        b.Status = CardReconciliationBatchStatus.Posted;
        b.PostedAtUtc = DateTime.UtcNow;
        b.DetailJson = JsonSerializer.Serialize(new
        {
            priorDetailJson = b.DetailJson,
            paymentCount = payments.Count,
            paymentTotal,
            callCount = calls.Count,
            callTotal,
            paymentIds = payments.Select(p => p.Id),
            callIds
        });
        ApiAudit.Write(db, HttpContext, "cards", "reconciliation_batch_posted", $"CardReconciliationBatch:{id}",
            new { from = CardReconciliationBatchStatus.Open, to = b.Status, paymentCount = payments.Count, callCount = calls.Count });
        await db.SaveChangesAsync(ct);
        return Ok(new { b.Id, b.Status, paymentCount = payments.Count, callCount = calls.Count, paymentTotal, callTotal });
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

        if (!ValidObservation(body.PanLast4, body.ReportedCardType))
            return BadRequest(new { error = "Supply a valid card type and optional last four ASCII digits." });
        var account = await FindCardObservationAccountAsync(body.CardAccountId, ct);
        if (body.CardAccountId != null && account == null)
            return BadRequest(new { error = "Unknown CardAccountId" });
        var ev = new CardWriteEvent
        {
            CardAccountId = body.CardAccountId,
            PanLast4 = ObservationLast4(body.PanLast4, account),
            ReportedCardType = body.ReportedCardType ?? account?.ResolvedCardType ?? CardType.Unknown,
            Disposition = CardWriteDisposition.Simulated,
            SimulationMode = true
        };
        db.CardWriteEvents.Add(ev);
        ApiAudit.Write(db, HttpContext, "cards", "card_write_simulated", $"CardWriteEvent:{ev.Id}",
            new { disposition = ev.Disposition, reportedCardType = ev.ReportedCardType });
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

    // Unknown fields from older clients, such as raw payloads, are ignored by JSON binding.
    public sealed record CardReadEventDto(Guid? CardAccountId, Guid? TerminalId, CardType? ReportedCardType,
        string? PanLast4 = null, CardReadResult? Result = null);

    public sealed record PaymentTransactionDto(
        Guid CardAccountId,
        decimal Amount,
        ReconciliationStatus? Reconciliation,
        CardType? ReportedCardType);

    public sealed record ReconciliationBatchCreateDto(string? DetailJson);

    public sealed record ReconciliationCloseDto(bool Confirm);

    public sealed record ReconciliationExceptionDto(bool Confirm, string? Note);

    public sealed record CardWriteEventDto(Guid? CardAccountId, bool SimulationMode,
        string? PanLast4 = null, CardType? ReportedCardType = null);

    private static bool ValidObservation(string? last4, CardType? cardType) =>
        (string.IsNullOrEmpty(last4) || CardIdentifierRedaction.IsLast4(last4)) &&
        (cardType == null || Enum.IsDefined(cardType.Value));

    private Task<CardAccount?> FindCardObservationAccountAsync(Guid? id, CancellationToken ct) =>
        id == null ? Task.FromResult<CardAccount?>(null) : db.CardAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);

    private static string? ObservationLast4(string? last4, CardAccount? account) =>
        CardIdentifierRedaction.IsLast4(last4) ? last4 : CardIdentifierRedaction.IsLast4(account?.PanLast4) ? account!.PanLast4 : null;

    private static BatchSummary ReadBatchSummary(string? detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson))
            return new BatchSummary(0, 0, 0, 0);
        try
        {
            using var doc = JsonDocument.Parse(detailJson);
            var root = doc.RootElement;
            return new BatchSummary(
                ReadInt(root, "paymentCount"),
                ReadInt(root, "callCount"),
                ReadDecimal(root, "paymentTotal"),
                ReadDecimal(root, "callTotal"));
        }
        catch (JsonException)
        {
            return new BatchSummary(0, 0, 0, 0);
        }
    }

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var n) ? n : 0;

    private static decimal ReadDecimal(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDecimal(out var n) ? n : 0;

    private readonly record struct BatchSummary(int PaymentCount, int CallCount, decimal PaymentTotal, decimal CallTotal);
}
