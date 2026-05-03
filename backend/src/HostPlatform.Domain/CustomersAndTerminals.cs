namespace HostPlatform.Domain;

public class Customer : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public ICollection<Site> Sites { get; set; } = new List<Site>();
}

public class Site : AuditableEntity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public ICollection<Terminal> Terminals { get; set; } = new List<Terminal>();
}

public class TerminalGroup : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public ICollection<Terminal> Terminals { get; set; } = new List<Terminal>();
}

public class FirmwareVersion : AuditableEntity
{
    public string Label { get; set; } = string.Empty;
    public string? BuildId { get; set; }
    public string? Notes { get; set; }
}

public class TransportEndpoint : AuditableEntity
{
    public TransportKind Kind { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty; // redact in logs
}

public class Terminal : AuditableEntity
{
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public Guid? TerminalGroupId { get; set; }
    public TerminalGroup? TerminalGroup { get; set; }
    public Guid? TransportEndpointId { get; set; }
    public TransportEndpoint? TransportEndpoint { get; set; }
    public Guid? FirmwareVersionId { get; set; }
    public FirmwareVersion? FirmwareVersion { get; set; }
    /// <summary>5-byte terminal id on wire (NCC termid); stored as hex for UI.</summary>
    public string TerminalIdHex { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public TerminalOperationalStatus Status { get; set; } = TerminalOperationalStatus.Provisioned;
    public ICollection<TerminalEvent> Events { get; set; } = new List<TerminalEvent>();
}

public class TerminalEvent : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>Posted terminal status snapshot (distinct from enum on <see cref="Terminal"/>).</summary>
public class TerminalStatusRecord : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public TerminalOperationalStatus Status { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
}
