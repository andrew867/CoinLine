using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostPlatform.Domain;

namespace HostPlatform.Api.Hosting;

// Safe, typed audit metadata only. Neither protected card bytes nor a
// provider-supplied reference or message is persisted or returned here.
internal sealed record CardAuthorizationAuditDetail(byte MessageType, string Outcome, string Provider,
    string? Reason = null, Guid? CardAccountId = null, Guid? QuoteId = null, decimal? QuotedAmountUsd = null,
    ushort? CallSequence = null, Guid? OriginalDecisionId = null, int? RequestBytes = null, int? BundledCallRecords = null,
    int[]? SuffixCdrTypes = null, string? RateSource = null, Guid? RatePlanVersionId = null);

internal static class CardAuthorizationAudit
{
    public const string DecisionAction = "card_authorization_decision";
    public const string ReplayAction = "card_authorization_replay";
    public const string LabProvider = "synthetic_lab";
    public const string ShadyBankProvider = "shadybank";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly HashSet<string> Outcomes =
    [
        "approved", "declined", "provider_unavailable", "provider_invalid_decision", "provider_failed", "provider_timeout", "provider_http_error",
        "audit_failed", "malformed_request", "unknown"
    ];
    private static readonly HashSet<string> Providers = [LabProvider, ShadyBankProvider, "trusted_provider", "none", "unrecorded"];
    private static readonly HashSet<string> BankRateSources = ["native_quote", "authorization_only"];
    private static readonly HashSet<string> Reasons =
    [
        "fixture_not_recognized", "binding_not_found", "account_not_eligible", "quote_missing",
        "quote_not_eligible", "insufficient_balance", "quote_already_used", "approved", "bank_approved", "bank_declined"
    ];

    public static string Encode(CardAuthorizationAuditDetail detail) => JsonSerializer.Serialize(detail, Json);

    public static CardAuthorizationAuditDetail? Read(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("messageType", out var mt)
                || mt.ValueKind != JsonValueKind.Number || !mt.TryGetByte(out var messageType) || messageType is not (1 or 2))
                return null;
            var outcome = SafeString(root, "outcome", Outcomes) ?? "unknown";
            var provider = SafeString(root, "provider", Providers) ?? "unrecorded";
            var requestBytes = SafeCount(root, "requestBytes", 1, HostPlatform.Protocols.Ncc.NccConstants.MaxDataSize);
            var bundledRecords = SafeCount(root, "bundledCallRecords", 0, 2);
            int[]? suffix = null;
            if (outcome == "malformed_request" && root.TryGetProperty("suffixCdrTypes", out var types) &&
                types.ValueKind == JsonValueKind.Array && types.GetArrayLength() <= 3 &&
                types.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var type) && type is 4 or 53 or 61))
                suffix = types.EnumerateArray().Select(item => item.GetInt32()).ToArray();
            // Only the built-in providers can produce typed quote evidence;
            // only the synthetic provider can identify a local ledger account.
            if (provider != LabProvider && provider != ShadyBankProvider)
                return new(messageType, outcome, provider, RequestBytes: requestBytes, BundledCallRecords: bundledRecords, SuffixCdrTypes: suffix);
            var amount = root.TryGetProperty("quotedAmountUsd", out var value)
                && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var money) && money >= 0 ? (decimal?)money : null;
            var sequence = root.TryGetProperty("callSequence", out value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetUInt16(out var seq) ? (ushort?)seq : null;
            return new(messageType, outcome, provider, SafeString(root, "reason", Reasons),
                provider == LabProvider ? SafeGuid(root, "cardAccountId") : null, SafeGuid(root, "quoteId"), amount, sequence,
                provider == LabProvider ? SafeGuid(root, "originalDecisionId") : null, requestBytes, bundledRecords, suffix,
                provider == ShadyBankProvider ? SafeString(root, "rateSource", BankRateSources) : null,
                provider == ShadyBankProvider ? SafeGuid(root, "ratePlanVersionId") : null);
        }
        catch (JsonException) { return null; }
    }

    public static byte? RedactedRequestType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("redactedMessageType", out var type) && type.ValueKind == JsonValueKind.Number
                && type.TryGetByte(out var number) && number is 1 or 2 ? number : null;
        }
        catch (JsonException) { return null; }
    }

    public static Guid DecisionIdForQuote(Guid quoteId)
    {
        // Use only the host's random quote identity, never any card material.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("coinline-synthetic-authorization:" + quoteId.ToString("N")));
        return new Guid(bytes.AsSpan(0, 16));
    }

    public static ReadOnlyMemory<byte> LabReference(byte messageType, Guid decisionId) =>
        messageType == 2 ? Encoding.ASCII.GetBytes("L" + decisionId.ToString("N")[..7].ToUpperInvariant()) : ReadOnlyMemory<byte>.Empty;

    public static bool TryReplay(AuditEvent? row, Guid terminalId, byte messageType,
        SyntheticAuthorizationEvidence candidate, out NccCardAuthorizationDecision decision)
    {
        decision = new();
        if (row == null || row.Id != candidate.DecisionId || row.TerminalId != terminalId
            || row.Category != "ncc" || row.Action != DecisionAction || Read(row.DetailJson) is not { } previous
            || previous.Provider != LabProvider || previous.MessageType != messageType
            || previous.CardAccountId != candidate.CardAccountId || previous.QuoteId != candidate.QuoteId
            || previous.CallSequence != candidate.CallSequence || previous.QuotedAmountUsd != candidate.QuotedAmountUsd
            || previous.Outcome is not ("approved" or "declined") || previous.Reason == null)
            return false;
        var approved = previous.Outcome == "approved";
        decision = new()
        {
            Approved = approved,
            AuthorizationReference = approved ? LabReference(messageType, row.Id) : ReadOnlyMemory<byte>.Empty,
            SyntheticEvidence = candidate with { Reason = previous.Reason, Replayed = true }
        };
        return true;
    }

    private static string? SafeString(JsonElement root, string key, HashSet<string> allowed) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text && allowed.Contains(text) ? text : null;

    private static Guid? SafeGuid(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            && value.TryGetGuid(out var id) && id != Guid.Empty ? id : null;

    private static int? SafeCount(JsonElement root, string key, int minimum, int maximum) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number) && number >= minimum && number <= maximum ? number : null;
}
