using HostPlatform.SipModem;
using Microsoft.AspNetCore.Mvc;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/sip-modem")]
public sealed class SipModemController(SipModemHostedService modem) : ControllerBase
{
    [HttpGet]
    public ActionResult<SipModemSnapshot> Get() => Ok(modem.Snapshot);

    /// <summary>
    /// Places a loopback G.711 + Bell 202 call against this host's SIP modem.
    /// Decoded bytes go to the NCC listener. This is not a PSTN call.
    /// </summary>
    [HttpPost("self-test")]
    public async Task<ActionResult<SipModemSelfTestResult>> SelfTest(CancellationToken ct)
    {
        var result = await modem.RunSelfTestAsync(ct);
        return result.Ok ? Ok(result) : BadRequest(result);
    }

    /// <summary>Ends every active SIP call on this host's audio modem.</summary>
    [HttpPost("hangup")]
    public async Task<ActionResult<SipModemHangupResult>> Hangup(CancellationToken ct)
    {
        var result = await modem.HangupAllAsync(ct);
        return result.Ok ? Ok(result) : BadRequest(result);
    }
}
