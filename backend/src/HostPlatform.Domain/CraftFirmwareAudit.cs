namespace HostPlatform.Domain;

public class CraftSession : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    /// <summary>Technician / field actor identity (caller-supplied; integrate with IdP in production).</summary>
    public string TechnicianId { get; set; } = string.Empty;
    /// <summary>Supervisor or dispatch operator id for paired accountability.</summary>
    public string OperatorId { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    /// <summary>Free-form session notes (lab ticket, site id).</summary>
    public string FieldNotes { get; set; } = string.Empty;
    public ICollection<CraftCommand> Commands { get; set; } = new List<CraftCommand>();
    public ICollection<CraftAuditEvent> AuditTrail { get; set; } = new List<CraftAuditEvent>();
}

public class CraftCommand : AuditableEntity
{
    public Guid CraftSessionId { get; set; }
    public CraftSession? CraftSession { get; set; }
    public Guid? CraftCommandTypeId { get; set; }
    public CraftCommandType? CraftCommandType { get; set; }
    public CraftCommandStatus Status { get; set; } = CraftCommandStatus.Queued;
    /// <summary>Opaque craft/NCC payload bytes — preserved verbatim for replay/logging.</summary>
    public byte[] RequestRaw { get; set; } = Array.Empty<byte>();
    public byte[]? ResponseRaw { get; set; }
    public string CommandName { get; set; } = string.Empty;
    /// <summary>Required audit justification when registry marks command destructive.</summary>
    public string AuditReason { get; set; } = string.Empty;
    public bool DestructiveConfirmed { get; set; }
    /// <summary>When true, execution stays on host simulation stub — live terminal I/O requires proven integration.</summary>
    public bool SimulationExecution { get; set; } = true;
}

public class CraftAuditEvent : AuditableEntity
{
    public Guid CraftSessionId { get; set; }
    public CraftSession? CraftSession { get; set; }
    public string Message { get; set; } = string.Empty;
    public string DetailJson { get; set; } = "{}";
    public DateTime OccurredAtUtc { get; set; }
}

public class FirmwarePackage : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string VersionLabel { get; set; } = string.Empty;
    /// <summary>Legacy aggregate checksum (full artifact); kept for backward compatibility with artifact registry.</summary>
    public byte[] ArtifactChecksum { get; set; } = Array.Empty<byte>();
    public long ArtifactSizeBytes { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public Guid? PrimaryArtifactId { get; set; }
    public FirmwareArtifact? PrimaryArtifact { get; set; }
    public ICollection<FirmwareArtifact> Artifacts { get; set; } = new List<FirmwareArtifact>();
    public ICollection<FirmwareCompatibilityRule> CompatibilityRules { get; set; } = new List<FirmwareCompatibilityRule>();
    public ICollection<FirmwareBlockManifest> BlockManifests { get; set; } = new List<FirmwareBlockManifest>();
}

public class FirmwareTarget : AuditableEntity
{
    public Guid FirmwarePackageId { get; set; }
    public FirmwarePackage? FirmwarePackage { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public class FirmwareUpdateJob : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public Guid FirmwarePackageId { get; set; }
    public FirmwarePackage? FirmwarePackage { get; set; }
    /// <summary>Optional explicit artifact selected for this job.</summary>
    public Guid? FirmwareArtifactId { get; set; }
    public FirmwareArtifact? FirmwareArtifact { get; set; }
    public FirmwareUpdateJobStatus Status { get; set; } = FirmwareUpdateJobStatus.Simulation;
    /// <summary>When true, no live flash/XMODEM execution — host simulation only.</summary>
    public bool SimulationMode { get; set; } = true;
    public string SafetyStateJson { get; set; } = "{}";
    public DateTime? ApprovedAtUtc { get; set; }
    public string ApprovedByOperatorId { get; set; } = string.Empty;
    public string CancelReason { get; set; } = string.Empty;
    public ICollection<FirmwareUpdateStep> Steps { get; set; } = new List<FirmwareUpdateStep>();
    public ICollection<FirmwareUpdateSafetyCheck> SafetyChecks { get; set; } = new List<FirmwareUpdateSafetyCheck>();
    public FirmwareRollBackPlan? RollBackPlan { get; set; }
}

public class FirmwareUpdateStep : AuditableEntity
{
    public Guid FirmwareUpdateJobId { get; set; }
    public FirmwareUpdateJob? FirmwareUpdateJob { get; set; }
    public int StepIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public FirmwareUpdateStepStatus StepStatus { get; set; } = FirmwareUpdateStepStatus.Pending;
    public bool Succeeded { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public class AuditEvent : AuditableEntity
{
    public string Category { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string DetailJson { get; set; } = "{}";
    public string? CorrelationId { get; set; }
    public string? TerminalSessionId { get; set; }
    public Guid? TerminalId { get; set; }
}
