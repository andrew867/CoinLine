using System.Collections.Concurrent;

namespace HostPlatform.Api.Hosting;

/// <summary>
/// Tracks bound NCC sockets so an operator action can write framed bytes onto
/// an active management call. Registration is per terminal; closing the link removes it.
/// </summary>
public interface INccSessionOutbound
{
    int ActiveLinks { get; }

    bool TryDescribe(Guid terminalId, out NccOutboundLinkInfo info);

    bool TryDescribeBySession(Guid nccSessionId, out NccOutboundLinkInfo info);

    bool TryGetTerminalWireId(Guid terminalId, out byte[] wireId);

    IReadOnlyCollection<Guid> BoundSessionIds();

    void Register(Guid terminalId, Guid nccSessionId, byte[] terminalIdWire,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeFrame);

    void Unregister(Guid nccSessionId);

    Task WriteFramedAsync(Guid terminalId, ReadOnlyMemory<byte> framed, CancellationToken cancellationToken);
}

public sealed record NccOutboundLinkInfo(Guid TerminalId, Guid NccSessionId, string TerminalIdHex);

public sealed class NccSessionOutboundHub : INccSessionOutbound
{
    private readonly ConcurrentDictionary<Guid, Entry> _byTerminal = new();
    private readonly ConcurrentDictionary<Guid, Guid> _sessionToTerminal = new();

    public int ActiveLinks => _byTerminal.Count;

    public bool TryDescribe(Guid terminalId, out NccOutboundLinkInfo info)
    {
        if (_byTerminal.TryGetValue(terminalId, out var entry))
        {
            info = new NccOutboundLinkInfo(terminalId, entry.NccSessionId, Convert.ToHexString(entry.TerminalIdWire));
            return true;
        }

        info = default!;
        return false;
    }

    public bool TryDescribeBySession(Guid nccSessionId, out NccOutboundLinkInfo info)
    {
        if (_sessionToTerminal.TryGetValue(nccSessionId, out var terminalId))
            return TryDescribe(terminalId, out info);

        info = default!;
        return false;
    }

    public IReadOnlyCollection<Guid> BoundSessionIds() => _sessionToTerminal.Keys.ToArray();

    public void Register(Guid terminalId, Guid nccSessionId, byte[] terminalIdWire,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> writeFrame)
    {
        ArgumentNullException.ThrowIfNull(terminalIdWire);
        ArgumentNullException.ThrowIfNull(writeFrame);
        if (terminalIdWire.Length != Protocols.Ncc.NccConstants.TerminalIdSize)
            throw new ArgumentException("Terminal wire id must be 5 bytes.", nameof(terminalIdWire));

        var entry = new Entry(nccSessionId, terminalIdWire.ToArray(), writeFrame);
        _byTerminal.AddOrUpdate(terminalId, entry, (_, _) => entry);
        _sessionToTerminal[nccSessionId] = terminalId;
    }

    public void Unregister(Guid nccSessionId)
    {
        if (!_sessionToTerminal.TryRemove(nccSessionId, out var terminalId))
            return;
        _byTerminal.TryRemove(terminalId, out _);
    }

    public Task WriteFramedAsync(Guid terminalId, ReadOnlyMemory<byte> framed, CancellationToken cancellationToken)
    {
        if (!_byTerminal.TryGetValue(terminalId, out var entry))
            throw new InvalidOperationException("No active NCC link for this terminal.");

        return entry.WriteFrame(framed, cancellationToken);
    }

    public bool TryGetTerminalWireId(Guid terminalId, out byte[] wireId)
    {
        if (_byTerminal.TryGetValue(terminalId, out var entry))
        {
            wireId = entry.TerminalIdWire;
            return true;
        }

        wireId = Array.Empty<byte>();
        return false;
    }

    private sealed record Entry(Guid NccSessionId, byte[] TerminalIdWire,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> WriteFrame);
}
