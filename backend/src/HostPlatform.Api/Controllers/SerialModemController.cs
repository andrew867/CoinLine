using HostPlatform.Api.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/serial-modem")]
public sealed class SerialModemController(SerialAtModemService modem) : ControllerBase
{
    [HttpGet]
    public ActionResult<SerialModemSnapshot> Get() => Ok(modem.Snapshot);
}
