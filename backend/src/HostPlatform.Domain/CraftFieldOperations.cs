namespace HostPlatform.Domain;

/// <summary>Registry row for craft opcode categories — semantics HARDWARE_VALIDATION_REQUIRED vs firmware image.</summary>
public class CraftCommandType : AuditableEntity
{
    /// <summary>Stable code e.g. craft.ping, craft.table_reload.</summary>
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Requires explicit technician acknowledgement before enqueue.</summary>
    public bool IsDestructive { get; set; }
    /// <summary>Default to simulation transport until live channel certified.</summary>
    public bool DefaultSimulationOnly { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}

/// <summary>Structured diagnostic captured during a craft session or field visit.</summary>
public class CraftDiagnostic : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public Guid? CraftSessionId { get; set; }
    public CraftSession? CraftSession { get; set; }
    public string Category { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime RecordedAtUtc { get; set; }
}

/// <summary>Point-in-time opaque diagnostic snapshot (VFD trace, keypad scan, etc.).</summary>
public class TerminalDiagnosticSnapshot : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string Source { get; set; } = "operator_ui";
}

/// <summary>Host intent to trigger remote table reload via craft/NCC — execution abstracted.</summary>
public class RemoteTableReloadRequest : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public TerminalFieldRequestStatus Status { get; set; } = TerminalFieldRequestStatus.Pending;
    public bool SimulationMode { get; set; } = true;
    public string DetailJson { get; set; } = "{}";
    public Guid? CraftSessionId { get; set; }
}

/// <summary>Host intent to upload CDR / maintenance queue — execution abstracted.</summary>
public class CdrUploadRequest : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public TerminalFieldRequestStatus Status { get; set; } = TerminalFieldRequestStatus.Pending;
    public bool SimulationMode { get; set; } = true;
    public string DetailJson { get; set; } = "{}";
    public Guid? CraftSessionId { get; set; }
}
