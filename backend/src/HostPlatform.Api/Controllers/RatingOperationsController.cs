using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Rating;
using HostPlatform.Rating;
using Microsoft.AspNetCore.Mvc;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/rating")]
public sealed class RatingOperationsController(HostPlatformDbContext db) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    [HttpPost("quote")]
    public async Task<ActionResult<object>> Quote([FromBody] QuoteBody body, CancellationToken ct)
    {
        var r = await RatingWorkflow.QuoteAsync(db, new RatingWorkflow.QuoteRequestDto(
            body.DialedDigits,
            body.Mode,
            body.RatePlanId,
            body.CustomerId,
            body.AssumedDurationMinutes ?? 1m,
            body.AsOfUtc), ct);
        return Ok(BuildQuoteResponse(r));
    }

    [HttpPost("authorize")]
    public async Task<ActionResult<object>> Authorize([FromBody] AuthorizeBody body, CancellationToken ct)
    {
        var quote = await RatingWorkflow.QuoteAsync(db, new RatingWorkflow.QuoteRequestDto(
            body.DialedDigits,
            body.Mode,
            body.RatePlanId,
            body.CustomerId,
            body.AssumedDurationMinutes ?? 1m,
            body.AsOfUtc), ct);

        var decisionKind = quote.DecisionKind;
        var allowed = quote.Allowed;
        if (quote.DecisionKind == RatingDecisionKind.Allowed
            && body.AvailableBalanceUsd is { } bal
            && quote.AmountUsd > bal)
        {
            decisionKind = RatingDecisionKind.InsufficientBalance;
            allowed = false;
        }

        var payload = new
        {
            quote = BuildQuoteResponse(quote),
            effectiveDecisionKind = decisionKind,
            allowed,
            insufficientBalance = decisionKind == RatingDecisionKind.InsufficientBalance,
            warning = "MVP simulated rating — card balance checks are mocked; not production parity."
        };

        db.CallAuthorizationRequests.Add(new CallAuthorizationRequest
        {
            TerminalId = body.TerminalId,
            RatePlanId = body.RatePlanId,
            DialedDigits = body.DialedDigits,
            AvailableBalanceUsd = body.AvailableBalanceUsd,
            RequestPayloadJson = JsonSerializer.Serialize(body, JsonOpts),
            DecisionPayloadJson = JsonSerializer.Serialize(payload, JsonOpts)
        });
        ApiAudit.Write(db, HttpContext, "rating", "rating_authorize", "CallAuthorizationRequest", new
        {
            body.TerminalId,
            body.RatePlanId,
            body.DialedDigits,
            effectiveDecisionKind = decisionKind,
            quoteDecisionKind = quote.DecisionKind
        });
        await db.SaveChangesAsync(ct);

        return Ok(payload);
    }

    private static object BuildQuoteResponse(RatingEngine.QuoteResult r) => new
    {
        amountUsd = r.AmountUsd,
        currency = "USD",
        decisionKind = r.DecisionKind,
        allowed = r.Allowed,
        blocked = r.Blocked,
        freeCall = r.FreeCall,
        emergency = r.Emergency,
        diagnostics = r.Diagnostics.Select(d => new { d.Code, d.Severity, d.Message }),
        determinismFingerprint = r.DeterminismFingerprint,
        determinismInputJson = r.DeterminismInputJson,
        warning = "MVP simulated rating — not production firmware parity. See host-platform/docs/rating-mvp.md."
    };

    public sealed record QuoteBody(
        string DialedDigits,
        RatingMode Mode,
        Guid? RatePlanId,
        Guid? CustomerId,
        decimal? AssumedDurationMinutes,
        DateTime? AsOfUtc);

    public sealed record AuthorizeBody(
        string DialedDigits,
        RatingMode Mode,
        Guid? RatePlanId,
        Guid? CustomerId,
        decimal? AssumedDurationMinutes,
        DateTime? AsOfUtc,
        decimal? AvailableBalanceUsd,
        Guid? TerminalId);
}
