namespace HostPlatform.Protocols.Dlog;

/// <summary>Immediate-clear authorization records for formatted and formatted/unformatted card calls.</summary>
public static class DlogCardAuthorizationWire
{
    private const byte FormattedRequest = 1;
    private const byte ExtendedRequest = 2;
    private const byte FormattedResponse = 3;
    private const byte ExtendedResponse = 65;
    private const byte Denied = 1;
    private const int FormattedRequestLength = 29;
    private const int ExtendedRequestLength = 39;
    private const int ExtendedResponseLength = 22;

    public static bool IsRequest(byte messageType) => messageType is FormattedRequest or ExtendedRequest;

    public static int RequestLength(byte messageType) => messageType switch
    {
        FormattedRequest => FormattedRequestLength,
        ExtendedRequest => ExtendedRequestLength,
        _ => 0
    };

    public static bool TryBuildDecline(ReadOnlySpan<byte> data, out byte[] response)
    {
        return TryBuildDecision(data, approved: false, [], out response);
    }

    /// <summary>Encode a decision supplied by a trusted payment provider, never a local approval guess.</summary>
    public static bool TryBuildDecision(ReadOnlySpan<byte> data, bool approved, ReadOnlySpan<byte> authorizationReference,
        out byte[] response)
    {
        response = [];
        if (data.Length == FormattedRequestLength && data[0] == FormattedRequest)
        {
            if (!authorizationReference.IsEmpty)
                return false;
            response = [FormattedResponse, approved ? (byte)0 : Denied];
        }
        else if (data.Length == ExtendedRequestLength && data[0] == ExtendedRequest)
        {
            if (approved ? authorizationReference.Length != 8 : !authorizationReference.IsEmpty)
                return false;
            response = new byte[ExtendedResponseLength];
            response[0] = ExtendedResponse;
            response[1] = approved ? (byte)0 : Denied;
            if (approved)
                authorizationReference.CopyTo(response.AsSpan(2, 8));
        }

        return response.Length > 0;
    }
}
