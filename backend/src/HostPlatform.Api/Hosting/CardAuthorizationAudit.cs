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
    ushort? CallSequence = null, Guid? OriginalDecisionId = null);

internal static class CardAuthorizationAudit
{
    public const string DecisionAction = "card_authorization_decision";
    public const string ReplayAction = "card_authorization_replay";
    public const string LabProvider = "synthetic_lab";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly HashSet<string> Outcomes =
    [
        "approved", "declined", "provider_unavailable", "provider_invalid_decision", "provider_failed",
        "audit_failed", "malformed_request", "unknown"
    ];
    private static readonly HashSet<string> Providers = ["synthetic_lab", "trusted_provider", "none", "unrecorded"];
    private static readonly HashSet<string> Reasons =
    [
        "fixture_not_recognized", "binding_not_found", "account_not_eligible", "quote_missing",
        "quote_not_eligible", "insufficient_balance", "quote_already_used", "approved"
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
            // Only the built-in synthetic provider can produce account/quote
            // evidence meaningful to this ledger.
            if (provider != LabProvider)
                return new(messageType, outcome, provider);
            var amount = root.TryGetProperty("quotedAmountUsd", out var value)
                && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var money) && money >= 0 ? (decimal?)money : null;
            var sequence = root.TryGetProperty("callSequence", out value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetUInt16(out var seq) ? (ushort?)seq : null;
            return new(messageType, outcome, provider, SafeString(root, "reason", Reasons),
                SafeGuid(root, "cardAccountId"), SafeGuid(root, "quoteId"), amount, sequence,
                SafeGuid(root, "originalDecisionId"));
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
}
