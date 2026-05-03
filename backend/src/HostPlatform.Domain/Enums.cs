namespace HostPlatform.Domain;

public enum TerminalOperationalStatus
{
    Unknown = 0,
    Provisioned = 1,
    Online = 2,
    Offline = 3,
    Maintenance = 4,
    Decommissioned = 5
}

public enum TransportKind
{
    Unknown = 0,
    Serial = 1,
    Tcp = 2,
    Udp = 3,
    ModemPool = 4
}

public enum DownloadBatchStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    /// <summary>Building ordered items and validation (no terminal I/O yet).</summary>
    Preparing = 5
}

public enum UploadBatchStatus
{
    Received = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Quarantined = 4
}

public enum RatingMode
{
    Unknown = 0,
    RealTimeRated = 1,
    SetRated = 2,
    TableRated = 3
}

public enum CallDisposition
{
    Unknown = 0,
    Completed = 1,
    Blocked = 2,
    FreeCall = 3,
    Emergency = 4,
    Failed = 5
}

public enum ReconciliationStatus
{
    Unknown = 0,
    Pending = 1,
    Matched = 2,
    Exception = 3
}

public enum CraftCommandStatus
{
    Queued = 0,
    Sent = 1,
    Succeeded = 2,
    Failed = 3,
    TimedOut = 4,
    /// <summary>In-flight on host transport stub — live modem/NCC path HARDWARE_VALIDATION_REQUIRED.</summary>
    Running = 5,
    Cancelled = 6
}

/// <summary>Host-side intent for CDR upload / table reload requests — terminal ACK path not modeled here.</summary>
public enum TerminalFieldRequestStatus
{
    Pending = 0,
    AcknowledgedHost = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>Lifecycle for <see cref="FirmwareUpdateJob"/> (also called firmware update status in operator docs).</summary>
public enum FirmwareUpdateJobStatus
{
    Draft = 0,
    Simulation = 1,
    /// <summary>HARDWARE_VALIDATION_REQUIRED before enabling in production.</summary>
    PendingApproval = 2,
    Running = 3,
    Completed = 4,
    Failed = 5,
    RolledBack = 6,
    Cancelled = 7
}

/// <summary>Per-step execution state — host orchestrator only until DLA transport is certified.</summary>
public enum FirmwareUpdateStepStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4
}

public enum OperatorRole
{
    Viewer = 0,
    Operator = 1,
    Technician = 2,
    Admin = 3
}

/// <summary>Logical card rail aligned with firmware routes (MT 134 mag, MT 93 SC, Mondex, EPurse spare table, …).</summary>
public enum CardType
{
    Unknown = 0,
    Magstripe = 1,
    Smartcard = 2,
    EPurse = 3,
    Mondex = 4,
    SmartCity = 5,
    Proton = 6
}

public enum CardCredentialKind
{
    OpaqueToken = 0,
    TestFixture = 1,
    VaultReference = 2
}

public enum CardWriteDisposition
{
    Simulated = 0,
    BlockedHardwareValidationRequired = 1
}

public enum CardReconciliationBatchStatus
{
    Open = 0,
    Posted = 1,
    Closed = 2,
    Exception = 3
}
