using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HostPlatform.Domain;

namespace HostPlatform.Rating;

/// <summary>
/// Deterministic rating from operator-authored rules plus optional tariff catalog (destination prefixes and time bands).
/// Unknown digits default to <see cref="RatingDecisionKind.DeniedUnknownPrefix"/> (not silently allowed).
/// </summary>
public static class RatingEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public sealed record QuoteRequest(
        string DialedDigits,
        RatingMode Mode,
        Guid? RatePlanId,
        decimal AssumedDurationMinutes,
        DateTime AsOfUtc,
        IReadOnlyList<DialedNumberClass>? NumberClasses,
        RatePlanVersion? PublishedVersion);

    public sealed record QuoteResult(
        decimal AmountUsd,
        RatingDecisionKind DecisionKind,
        bool Allowed,
        bool Blocked,
        bool FreeCall,
        bool Emergency,
        IReadOnlyList<Diagnostic> Diagnostics,
        string DeterminismFingerprint,
        string DeterminismInputJson,
        RatingAirtimeSource AirtimeSource);

    public sealed record Diagnostic(string Code, string Severity, string Message);

    public static QuoteResult Quote(QuoteRequest req)
    {
        var diagnostics = new List<Diagnostic>();
        var digits = NormalizeDigits(req.DialedDigits);
        if (digits.Length == 0)
        {
            diagnostics.Add(new Diagnostic("EMPTY_DIAL", "Error", "No dialable digits after normalization."));
            return Finish(RatingDecisionKind.Blocked, false, true, false, false, 0m, req, diagnostics);
        }

        var classes = (req.NumberClasses ?? Array.Empty<DialedNumberClass>())
            .OrderBy(c => c.SortOrder)
            .ThenByDescending(c => c.Pattern.Length)
            .ToList();
        foreach (var nc in classes)
        {
            if (!Matches(nc.MatchKind, nc.Pattern, digits))
                continue;
            if (nc.IsBlocked)
            {
                diagnostics.Add(new Diagnostic("NUMBER_CLASS", "Info", $"Matched class '{nc.ClassName}' — blocked."));
                return Finish(RatingDecisionKind.Blocked, false, true, false, false, 0m, req, diagnostics);
            }

            if (nc.IsEmergency)
            {
                diagnostics.Add(new Diagnostic("NUMBER_CLASS", "Info", $"Matched class '{nc.ClassName}' — emergency (zero charge)."));
                return Finish(RatingDecisionKind.Emergency, true, false, false, true, 0m, req, diagnostics);
            }

            if (nc.IsFree)
            {
                diagnostics.Add(new Diagnostic("NUMBER_CLASS", "Info", $"Matched class '{nc.ClassName}' — free call."));
                return Finish(RatingDecisionKind.FreeCall, true, false, true, false, 0m, req, diagnostics);
            }
        }

        if (req.Mode == RatingMode.Unknown || req.RatePlanId == null || req.PublishedVersion == null)
        {
            diagnostics.Add(new Diagnostic("NO_PLAN", "Error", "Rate plan or published version missing — cannot quote."));
            return Finish(RatingDecisionKind.DeniedUnknownPrefix, false, false, false, false, 0m, req, diagnostics);
        }

        var rules = req.PublishedVersion.Rules.OrderByDescending(r => r.Priority).ThenByDescending(r => r.Pattern.Length).ToList();
        foreach (var rule in rules)
        {
            if (!Matches(rule.MatchKind, rule.Pattern, digits))
                continue;
            switch (rule.Outcome)
            {
                case RateRuleOutcome.Block:
                    diagnostics.Add(new Diagnostic("RATE_RULE", "Info", $"Rule matched (block): {rule.Pattern}"));
                    return Finish(RatingDecisionKind.Blocked, false, true, false, false, 0m, req, diagnostics);
                case RateRuleOutcome.Free:
                    diagnostics.Add(new Diagnostic("RATE_RULE", "Info", $"Rule matched (free): {rule.Pattern}"));
                    return Finish(RatingDecisionKind.FreeCall, true, false, true, false, 0m, req, diagnostics);
                case RateRuleOutcome.Emergency:
                    diagnostics.Add(new Diagnostic("RATE_RULE", "Info", $"Rule matched (emergency): {rule.Pattern}"));
                    return Finish(RatingDecisionKind.Emergency, true, false, false, true, 0m, req, diagnostics);
                case RateRuleOutcome.Rated:
                default:
                {
                    var minutes = Math.Max(0.01m, req.AssumedDurationMinutes);
                    var amount = Math.Round(rule.RatePerMinuteUsd * minutes, 4, MidpointRounding.AwayFromZero);
                    diagnostics.Add(new Diagnostic(
                        "RATE_RULE",
                        "Info",
                        $"Rule matched (rated): {rule.Pattern} @ {rule.RatePerMinuteUsd}/min × {minutes} min."));
                    AppendModeDiagnostics(req.Mode, diagnostics);
                    return Finish(RatingDecisionKind.Allowed, true, false, false, false, amount, req, diagnostics,
                        RatingAirtimeSource.RateRule);
                }
            }
        }

        var catalog = TryQuoteTariffCatalog(req, digits, diagnostics);
        if (catalog != null)
            return catalog;

        diagnostics.Add(new Diagnostic(
            "UNKNOWN_PREFIX",
            "Warning",
            "No rate rule or tariff catalog entry matched — default deny (not silently allowed)."));
        return Finish(RatingDecisionKind.DeniedUnknownPrefix, false, false, false, false, 0m, req, diagnostics);
    }

    private static QuoteResult? TryQuoteTariffCatalog(QuoteRequest req, string digits, List<Diagnostic> diagnostics)
    {
        var version = req.PublishedVersion!;
        if (version.DestinationPrefixes.Count == 0 && version.TimeBands.Count == 0)
            return null;

        var minutes = Math.Max(0.01m, req.AssumedDurationMinutes);

        Tariff? destTariff = null;
        DestinationPrefix? destRow = null;
        foreach (var dp in version.DestinationPrefixes
                     .OrderByDescending(x => NormalizeDigits(x.PrefixDigits).Length))
        {
            var p = NormalizeDigits(dp.PrefixDigits);
            if (p.Length == 0)
                continue;
            if (!digits.StartsWith(p, StringComparison.Ordinal))
                continue;
            destTariff = dp.Tariff;
            destRow = dp;
            break;
        }

        var band = FindMatchingTimeBand(version.TimeBands, req.AsOfUtc);

        Tariff? rateTariff = null;
        RatingAirtimeSource src = RatingAirtimeSource.None;
        if (band is { Tariff: { } bt })
        {
            rateTariff = bt;
            src = RatingAirtimeSource.TimeBand;
            diagnostics.Add(new Diagnostic(
                "TARIFF_TIME_BAND",
                "Info",
                $"Time band selected (mask {band.DayOfWeekMask}, minutes {band.StartMinuteOfDay}–{band.EndMinuteOfDay}, UTC wall-clock)."));
        }

        if (rateTariff == null && destTariff != null)
        {
            rateTariff = destTariff;
            src = RatingAirtimeSource.DestinationPrefix;
            diagnostics.Add(new Diagnostic(
                "TARIFF_DESTINATION",
                "Info",
                $"Destination prefix '{NormalizeDigits(destRow!.PrefixDigits)}' → tariff '{rateTariff.Name}'."));
        }

        if (rateTariff == null)
            return null;

        var amount = Math.Round(rateTariff.RatePerMinuteUsd * minutes, 4, MidpointRounding.AwayFromZero);
        diagnostics.Add(new Diagnostic(
            "TARIFF_RATE",
            "Info",
            $"Catalog airtime @ {rateTariff.RatePerMinuteUsd}/min × {minutes} min."));
        AppendModeDiagnostics(req.Mode, diagnostics);
        return Finish(RatingDecisionKind.Allowed, true, false, false, false, amount, req, diagnostics, src);
    }

    private static void AppendModeDiagnostics(RatingMode mode, List<Diagnostic> diagnostics)
    {
        if (mode == RatingMode.TableRated)
        {
            diagnostics.Add(new Diagnostic(
                "HARDWARE_VALIDATION_REQUIRED",
                "Warning",
                "Table-rated mode: host tariff catalog applied; terminal-side MT rate table selection is not asserted in this quote."));
        }
        else if (mode is RatingMode.SetRated)
        {
            diagnostics.Add(new Diagnostic(
                "SET_RATED_HOST",
                "Info",
                "Set-rated mode: host-published plan version drives this quote (terminal table push not asserted here)."));
        }
    }

    /// <summary>First matching band by stable sort — time-band tariff wins over destination when both exist.</summary>
    internal static TimeBand? FindMatchingTimeBand(IEnumerable<TimeBand> bands, DateTime asOfUtc)
    {
        var utc = asOfUtc.Kind == DateTimeKind.Utc ? asOfUtc : asOfUtc.ToUniversalTime();
        var dow = utc.DayOfWeek;
        var minuteOfDay = utc.Hour * 60 + utc.Minute;
        foreach (var b in bands.OrderBy(x => x.StartMinuteOfDay).ThenBy(x => x.Id))
        {
            if (!DayMatchesMask(b.DayOfWeekMask, dow))
                continue;
            if (!MinuteInBand(b.StartMinuteOfDay, b.EndMinuteOfDay, minuteOfDay))
                continue;
            return b;
        }

        return null;
    }

    internal static bool DayMatchesMask(int mask, DayOfWeek dow)
    {
        var bit = 1 << (int)dow;
        return (mask & bit) != 0;
    }

    internal static bool MinuteInBand(int start, int end, int minuteOfDay)
    {
        minuteOfDay = Math.Clamp(minuteOfDay, 0, 1439);
        if (start == end)
            return false;
        if (start < end)
            return minuteOfDay >= start && minuteOfDay < end;
        return minuteOfDay >= start || minuteOfDay < end;
    }

    public static bool Matches(RateRuleMatchKind kind, string pattern, string digits)
    {
        if (string.IsNullOrEmpty(pattern))
            return false;
        return kind switch
        {
            RateRuleMatchKind.Exact => digits == NormalizeDigits(pattern),
            RateRuleMatchKind.Regex => SafeRegexIsMatch(digits, pattern),
            _ => digits.StartsWith(NormalizeDigits(pattern), StringComparison.Ordinal)
        };
    }

    private static bool SafeRegexIsMatch(string digits, string pattern)
    {
        try
        {
            return Regex.IsMatch(digits, pattern, RegexOptions.CultureInvariant);
        }
        catch
        {
            return false;
        }
    }

    public static string NormalizeDigits(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (c is >= '0' and <= '9')
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static QuoteResult Finish(
        RatingDecisionKind kind,
        bool allowed,
        bool blocked,
        bool freeCall,
        bool emergencyCall,
        decimal amount,
        QuoteRequest req,
        List<Diagnostic> diagnostics,
        RatingAirtimeSource airtimeSource = RatingAirtimeSource.None)
    {
        var inputObj = new
        {
            digits = NormalizeDigits(req.DialedDigits),
            req.Mode,
            req.RatePlanId,
            req.AssumedDurationMinutes,
            req.AsOfUtc,
            versionId = req.PublishedVersion?.Id,
            decision = kind.ToString(),
            airtimeSource = airtimeSource.ToString()
        };
        var inputJson = JsonSerializer.Serialize(inputObj, JsonOpts);
        var fp = Fingerprint(inputJson, diagnostics);
        return new QuoteResult(amount, kind, allowed, blocked, freeCall, emergencyCall, diagnostics, fp, inputJson,
            airtimeSource);
    }

    private static string Fingerprint(string inputJson, IReadOnlyList<Diagnostic> diagnostics)
    {
        var diagBlob = string.Join("|", diagnostics.Select(d => $"{d.Code}:{d.Severity}:{d.Message}"));
        var bytes = Encoding.UTF8.GetBytes(inputJson + "\n" + diagBlob);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
