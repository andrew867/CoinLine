namespace HostPlatform.Transport;

/// <summary>Coarse modem link states for diagnostics and NCC session modeling (not a full AT command engine).</summary>
public enum ModemLinkState
{
    Idle = 0,
    Dialing = 1,
    Ringing = 2,
    Connected = 3,
    Busy = 4,
    NoAnswer = 5,
    CarrierLost = 6,
    Error = 7
}

/// <summary>Typed transitions for unit tests and session audit (legal transition coverage).</summary>
public static class ModemStateMachine
{
    public static bool TryTransition(ModemLinkState current, ModemLinkState next, out string? reason)
    {
        reason = null;
        if (current == next)
            return true;
        // Any state may reset to Idle (hang up / clean close).
        if (next == ModemLinkState.Idle)
            return true;
        if (current == ModemLinkState.Idle && next is ModemLinkState.Dialing)
            return true;
        if (current == ModemLinkState.Dialing && next is ModemLinkState.Connected or ModemLinkState.Busy
            or ModemLinkState.NoAnswer or ModemLinkState.Ringing)
            return true;
        if (current == ModemLinkState.Ringing && next is ModemLinkState.Connected)
            return true;
        if (current == ModemLinkState.Connected && next is ModemLinkState.CarrierLost or ModemLinkState.Error
            or ModemLinkState.Idle)
            return true;
        if (current is ModemLinkState.CarrierLost or ModemLinkState.Error or ModemLinkState.Busy
            or ModemLinkState.NoAnswer)
        {
            if (next is ModemLinkState.Dialing or ModemLinkState.Idle)
                return true;
        }
        reason = $"Illegal transition {current} -> {next}.";
        return false;
    }
}
