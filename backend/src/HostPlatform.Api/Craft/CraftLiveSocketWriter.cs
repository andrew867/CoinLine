using System.Text;
using HostPlatform.Api.Hosting;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Ncc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Craft;

/// <summary>
/// Dual-gated opaque craft write: frames <see cref="CraftCommand.RequestRaw"/> as an NCC
/// message packet and sends it on the terminal's active management socket. No response decode.
/// </summary>
public sealed class CraftLiveSocketWriter(INccSessionOutbound outbound)
{
    public async Task ExecuteWriteAsync(HostPlatformDbContext db, CraftCommand command, Guid terminalId,
        CancellationToken cancellationToken)
    {
        if (command.RequestRaw.Length == 0)
            throw new InvalidOperationException("Live craft write requires a non-empty RequestRaw payload.");

        if (!outbound.TryGetTerminalWireId(terminalId, out var wireId))
            throw new InvalidOperationException(
                "No active NCC link for this terminal. Place a management call first, then retry live craft.");

        var framed = NccFrameCodec.Encode(new NccWirePacket
        {
            Control = 0,
            TerminalId = wireId,
            Data = command.RequestRaw
        });

        command.Status = CraftCommandStatus.Running;
        command.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await outbound.WriteFramedAsync(terminalId, framed, cancellationToken);
        }
        catch (Exception)
        {
            command.Status = CraftCommandStatus.Failed;
            command.ResponseRaw = Encoding.ASCII.GetBytes("LIVE_WRITE_FAILED");
            command.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }

        command.Status = CraftCommandStatus.Succeeded;
        command.ResponseRaw = Encoding.ASCII.GetBytes("LIVE_WRITE_OK");
        command.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
