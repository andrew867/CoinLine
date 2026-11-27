namespace HostPlatform.Domain;

/// <summary>Reviewed target configuration retained separately until terminal version evidence permits activation.</summary>
public sealed class FirmwareConfigurationTransition : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Guid CodeScheduleId { get; set; }
    public Guid CodeRecordVersionId { get; set; }
    public string CodeRecordSha256Hex { get; set; } = string.Empty;
    public Guid SourceFirmwareVersionId { get; set; }
    public Guid TargetFirmwareVersionId { get; set; }
    public Guid SourceAssignmentId { get; set; }
    public int SourceAssignmentRevision { get; set; }
    public Guid SourceTableSetId { get; set; }
    public Guid TargetTableSetId { get; set; }
    public string SourceSnapshot { get; set; } = string.Empty;
    public string TargetSnapshot { get; set; } = string.Empty;
    public Guid SourceObservationId { get; set; }
    public string SourceControllerRom { get; set; } = string.Empty;
    public string SourceControllerSoftware { get; set; } = string.Empty;
    public string TargetControllerRom { get; set; } = string.Empty;
    public string TargetControllerSoftware { get; set; } = string.Empty;
    public bool AutoActivateOnContact { get; set; }
    public bool ActivatedAutomatically { get; set; }
    public Guid? TargetObservationId { get; set; }
    public Guid? TargetBatchId { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
}
