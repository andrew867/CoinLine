using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Api.Hosting;

internal static class TerminalStatusProfileResolver
{
    // A record-length-only profile does not establish its status-bit semantics.
    public static TerminalStatusProfile FromRegisteredProfile(string? profileId) => profileId switch
    {
        FirmwareTableProfiles.MultiPay200 => TerminalStatusProfile.Earlier,
        FirmwareTableProfiles.LaterMultiPay180 or FirmwareTableProfiles.GraphicalMultiPay180 => TerminalStatusProfile.Later,
        _ => TerminalStatusProfile.Unknown
    };
}
