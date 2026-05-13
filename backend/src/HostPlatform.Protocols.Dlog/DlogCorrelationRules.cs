namespace HostPlatform.Protocols.Dlog;

/// <summary>
/// Request/response pairs derived from OEM compatibility catalogue symbolic pairing (e.g. rate 63/64, maint 6/15).
/// Pairing by time/session remains heuristic — validate ordering against modem captures in lab (<see cref="HARDWARE_VALIDATION_REQUIRED"/>).
/// </summary>
public static class DlogCorrelationRules
{
    /// <summary>Documented request → response MT pairs (reference firmware OEM compatibility catalogue + payload catalogue).</summary>
    private static readonly (int Request, int Response)[] Pairs =
    {
        (1, 3),  // CARD_AUTH_REQ → AUTH_RESPONSE
        (2, 3),  // FUNF_CARD_AUTH → AUTH_RESPONSE
        (4, 5),  // CD_CALL_DETAILS → CDR_DETAILS_ACK
        (53, 5), // CALL_DETAILS → CDR_DETAILS_ACK (later #define in OEM compatibility catalogue)
        (6, 15), // MAINT_REQ → MAINT_ACK
        (7, 16), // ALARM → ALARM_ACK
        (63, 64) // RATE_REQUEST → RATE_RESPONSE
    };

    /// <summary>Stable catalogue for APIs and fixtures (same order as <see cref="Pairs"/>).</summary>
    public static IReadOnlyList<(int Request, int Response)> CompatibilityPairs => Pairs;

    /// <summary>Returns paired response MT when <paramref name="requestMessageType"/> is a known request; otherwise null.</summary>
    public static int? GetResponseMessageTypeForRequest(int requestMessageType)
    {
        foreach (var (req, resp) in Pairs)
        {
            if (req == requestMessageType)
                return resp;
        }

        return null;
    }

    public static IReadOnlyList<int> GetRequestMessageTypesForResponse(int responseMessageType)
    {
        var list = new List<int>();
        foreach (var (req, resp) in Pairs)
        {
            if (resp == responseMessageType)
                list.Add(req);
        }

        return list;
    }

    public const string HARDWARE_VALIDATION_REQUIRED =
        "HARDWARE_VALIDATION_REQUIRED: correlate by terminal + session + capture order; payload correlation keys not decoded without validated captures.";

    public const string OPEN_QUESTION =
        "OPEN QUESTION: confirm whether CDR ack (5) pairs with MT 4 only, 53 only, or both in a given firmware build.";
}
