using HostPlatform.Api.Options;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Firmware;

public interface IFirmwareExecutionPolicy
{
    bool AllowLiveFlashing { get; }
}

public sealed class FirmwareExecutionPolicy(IOptions<FirmwareOptions> options) : IFirmwareExecutionPolicy
{
    public bool AllowLiveFlashing => options.Value.AllowLiveFlashing;
}
