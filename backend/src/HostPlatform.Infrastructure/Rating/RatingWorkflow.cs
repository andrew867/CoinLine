using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Rating;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Rating;

/// <summary>Loads persisted configuration and runs <see cref="RatingEngine"/>.</summary>
public static class RatingWorkflow
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public sealed record QuoteRequestDto(
        string DialedDigits,
        RatingMode Mode,
        Guid? RatePlanId,
        Guid? CustomerId,
        decimal AssumedDurationMinutes,
        DateTime? AsOfUtc);

    public static async Task<RatingEngine.QuoteResult> QuoteAsync(
        HostPlatformDbContext db,
        QuoteRequestDto dto,
        CancellationToken ct)
    {
        var classes = await db.DialedNumberClasses.AsNoTracking()
            .Where(c => c.CustomerId == null || c.CustomerId == dto.CustomerId)
            .OrderBy(c => c.SortOrder)
            .ThenByDescending(c => c.Pattern.Length)
            .ToListAsync(ct);

        RatePlanVersion? published = null;
        Guid? planId = dto.RatePlanId;
        if (planId is { } pid)
        {
            var plan = await db.RatePlans.AsNoTracking()
                .Include(p => p.PublishedVersion!)!
                .ThenInclude(v => v!.Rules)
                .Include(p => p.PublishedVersion!)!
                .ThenInclude(v => v!.DestinationPrefixes)!.ThenInclude(d => d.Tariff)
                .Include(p => p.PublishedVersion!)!
                .ThenInclude(v => v!.TimeBands)!.ThenInclude(t => t.Tariff)
                .FirstOrDefaultAsync(p => p.Id == pid, ct);
            published = plan?.PublishedVersion;
        }

        return RatingEngine.Quote(new RatingEngine.QuoteRequest(
            dto.DialedDigits,
            dto.Mode,
            planId,
            dto.AssumedDurationMinutes,
            dto.AsOfUtc ?? DateTime.UtcNow,
            classes,
            published));
    }

    public static RatingResult ToPersistedResult(
        Guid callRecordId,
        RatingEngine.QuoteResult quote,
        Guid? appliedVersionId)
    {
        var segmentLabel = quote.AirtimeSource switch
        {
            RatingAirtimeSource.RateRule => "Airtime (rate rule)",
            RatingAirtimeSource.DestinationPrefix => "Airtime (destination tariff)",
            RatingAirtimeSource.TimeBand => "Airtime (time-of-day tariff)",
            _ => "Airtime"
        };

        var rr = new RatingResult
        {
            CallRecordId = callRecordId,
            Amount = quote.AmountUsd,
            Currency = "USD",
            Blocked = quote.Blocked,
            FreeCall = quote.FreeCall,
            Emergency = quote.Emergency,
            DecisionKind = quote.DecisionKind,
            RatePlanVersionId = appliedVersionId,
            DeterminismInputJson = quote.DeterminismInputJson,
            DetailJson = JsonSerializer.Serialize(new
            {
                quote.DecisionKind,
                quote.AmountUsd,
                quote.Allowed,
                quote.DeterminismFingerprint,
                airtimeSource = quote.AirtimeSource.ToString(),
                hostPlanQuote = true
            }, JsonOpts)
        };

        foreach (var d in quote.Diagnostics)
        {
            rr.Diagnostics.Add(new RatingDiagnostic
            {
                Code = d.Code,
                Severity = d.Severity,
                Message = d.Message
            });
        }

        if (quote.DecisionKind == RatingDecisionKind.Allowed && quote.AmountUsd > 0)
        {
            rr.Segments.Add(new CallChargeSegment
            {
                SegmentIndex = 0,
                Label = segmentLabel,
                AmountUsd = quote.AmountUsd
            });
        }

        return rr;
    }

    public static CallDisposition ToDisposition(RatingDecisionKind k) => k switch
    {
        RatingDecisionKind.Blocked => CallDisposition.Blocked,
        RatingDecisionKind.FreeCall => CallDisposition.FreeCall,
        RatingDecisionKind.Emergency => CallDisposition.Emergency,
        RatingDecisionKind.Allowed => CallDisposition.Completed,
        RatingDecisionKind.InsufficientBalance => CallDisposition.Failed,
        RatingDecisionKind.DeniedUnknownPrefix => CallDisposition.Blocked,
        RatingDecisionKind.PlaceholderTableRated => CallDisposition.Failed,
        _ => CallDisposition.Failed
    };
}
