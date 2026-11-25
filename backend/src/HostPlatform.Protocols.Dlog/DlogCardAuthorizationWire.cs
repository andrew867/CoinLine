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

    public static bool TryBuildDecline(ReadOnlySpan<byte> data, out byte[] response)
    {
        response = [];
        if (data.Length == FormattedRequestLength && data[0] == FormattedRequest)
        {
            response = [FormattedResponse, Denied];
        }
        else if (data.Length == ExtendedRequestLength && data[0] == ExtendedRequest)
        {
            response = new byte[ExtendedResponseLength];
            response[0] = ExtendedResponse;
            response[1] = Denied;
        }

        return response.Length > 0;
    }
}
