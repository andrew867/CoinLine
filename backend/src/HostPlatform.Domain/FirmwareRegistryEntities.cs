namespace HostPlatform.Domain;

/// <summary>Registered binary for a package — metadata + checksum; blob storage is out-of-band (HARDWARE_VALIDATION_REQUIRED).</summary>
public class FirmwareArtifact : AuditableEntity
{
    public Guid FirmwarePackageId { get; set; }
    public FirmwarePackage? FirmwarePackage { get; set; }
    /// <summary>e.g. primary, delta — opaque.</summary>
    public string Kind { get; set; } = "primary";
    /// <summary>SHA-256 hex (64 chars), authoritative integrity token.</summary>
    public string Sha256Hex { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    /// <summary>Opaque storage key / URI fragment — no live blob read in this tranche.</summary>
    public string StorageRef { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}

/// <summary>Host-side rule: terminal must satisfy constraints before a job is queued.</summary>
public class FirmwareCompatibilityRule : AuditableEntity
{
    public Guid FirmwarePackageId { get; set; }
    public FirmwarePackage? FirmwarePackage { get; set; }
    /// <summary>If set, terminal must reference this <see cref="FirmwareVersion"/> row.</summary>
    public Guid? RequiredTerminalFirmwareVersionId { get; set; }
    public FirmwareVersion? RequiredTerminalFirmwareVersion { get; set; }
    /// <summary>Optional substring match against <see cref="FirmwareTarget.Sku"/> for routing.</summary>
    public string? RequiredTargetSkuContains { get; set; }
    public string Notes { get; set; } = string.Empty;
}

/// <summary>DL_BLOCK-oriented manifest JSON — layout interpretation is HARDWARE_VALIDATION_REQUIRED.</summary>
public class FirmwareBlockManifest : AuditableEntity
{
    public Guid FirmwarePackageId { get; set; }
    public FirmwarePackage? FirmwarePackage { get; set; }
    public Guid? FirmwareArtifactId { get; set; }
    public FirmwareArtifact? FirmwareArtifact { get; set; }
    /// <summary>Opaque JSON — sector/block math per docs/protocols/firmware_update/flash_layout_sectors_and_dl_blocks.md.</summary>
    public string LayoutJson { get; set; } = "{}";
}

/// <summary>Operator-recorded rollback / backup intent — required before approval on non-simulation paths.</summary>
public class FirmwareRollBackPlan : AuditableEntity
{
    public Guid FirmwareUpdateJobId { get; set; }
    public FirmwareUpdateJob? FirmwareUpdateJob { get; set; }
    public string BackupNotes { get; set; } = string.Empty;
    public string RecoveryStepsJson { get; set; } = "{}";
}

/// <summary>Recorded gate outcome for audit (checksum, compatibility, simulation acknowledgement).</summary>
public class FirmwareUpdateSafetyCheck : AuditableEntity
{
    public Guid FirmwareUpdateJobId { get; set; }
    public FirmwareUpdateJob? FirmwareUpdateJob { get; set; }
    public string Code { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string DetailJson { get; set; } = "{}";
    public DateTime EvaluatedAtUtc { get; set; }
}
