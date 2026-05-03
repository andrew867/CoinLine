using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HostPlatform.Domain;

namespace HostPlatform.Rating;

/// <summary>
/// Deterministic MVP rating — <strong>not production parity</strong> until rules are UAT-backed.
/// Unknown numbers default to <see cref="RatingDecisionKind.DeniedUnknownPrefix"/> (not silently allowed).
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
        string DeterminismInputJson);

    public sealed record Diagnostic(string Code, string Severity, string Message);

    public static QuoteResult Quote(QuoteRequest req)
    {
        var digits = NormalizeDigits(req.DialedDigits);
        var diagnostics = new List<Diagnostic>();
        if (digits.Length == 0)
        {
            diagnostics.Add(new Diagnostic("EMPTY_DIAL", "Error", "No dialable digits after normalization."));
            return Finish(RatingDecisionKind.Blocked, false, true, false, false, 0m, req, diagnostics);
        }

        // Global / customer number-class overrides (blocked / free / emergency)
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

        if (req.Mode is RatingMode.SetRated or RatingMode.TableRated)
        {
            diagnostics.Add(new Diagnostic(
                "HARDWARE_VALIDATION_REQUIRED",
                "Warning",
                "Set-rated / table-rated paths require firmware-backed table selection — MVP returns placeholder only."));
            return Finish(RatingDecisionKind.PlaceholderTableRated, false, false, false, false, 0m, req, diagnostics);
        }

        if (req.Mode == RatingMode.Unknown || req.PublishedVersion == null || req.RatePlanId == null)
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
                    diagnostics.Add(new Diagnostic("RATE_RULE", "Info", $"Rule matched (rated): {rule.Pattern} @ {rule.RatePerMinuteUsd}/min × {minutes} min (MVP stub)."));
                    return Finish(RatingDecisionKind.Allowed, true, false, false, false, amount, req, diagnostics);
                }
            }
        }

        diagnostics.Add(new Diagnostic(
            "UNKNOWN_PREFIX",
            "Warning",
            "No rate rule or class matched — default deny (not silently allowed)."));
        return Finish(RatingDecisionKind.DeniedUnknownPrefix, false, false, false, false, 0m, req, diagnostics);
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
        List<Diagnostic> diagnostics)
    {
        var inputObj = new
        {
            digits = NormalizeDigits(req.DialedDigits),
            req.Mode,
            req.RatePlanId,
            req.AssumedDurationMinutes,
            req.AsOfUtc,
            versionId = req.PublishedVersion?.Id,
            decision = kind.ToString()
        };
        var inputJson = JsonSerializer.Serialize(inputObj, JsonOpts);
        var fp = Fingerprint(inputJson, diagnostics);
        return new QuoteResult(amount, kind, allowed, blocked, freeCall, emergencyCall, diagnostics, fp, inputJson);
    }

    private static string Fingerprint(string inputJson, IReadOnlyList<Diagnostic> diagnostics)
    {
        var diagBlob = string.Join("|", diagnostics.Select(d => $"{d.Code}:{d.Severity}:{d.Message}"));
        var bytes = Encoding.UTF8.GetBytes(inputJson + "\n" + diagBlob);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
