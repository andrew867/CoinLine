using HostPlatform.Domain;

namespace HostPlatform.Infrastructure;

public sealed class OperatorContext
{
    private static readonly AsyncLocal<OperatorContext?> _current = new();

    public static OperatorContext? Current => _current.Value;

    public static IDisposable Push(string operatorId, OperatorRole role)
    {
        var prev = _current.Value;
        _current.Value = new OperatorContext { OperatorId = operatorId, Role = role };
        return new Popper(prev);
    }

    public string OperatorId { get; private set; } = "dev";
    public OperatorRole Role { get; private set; } = OperatorRole.Admin;

    private sealed class Popper(OperatorContext? prev) : IDisposable
    {
        public void Dispose() => _current.Value = prev;
    }
}
