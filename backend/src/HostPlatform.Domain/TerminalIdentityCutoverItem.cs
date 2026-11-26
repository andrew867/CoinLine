namespace HostPlatform.Domain;

/// <summary>
/// A single phone's immutable planned identity change. A fleet job groups
/// these rows by JobId. Terminal and table assignment remain unchanged until
/// a subsequent connection confirms the reserved ID.
/// </summary>
public sealed class TerminalIdentityCutoverItem : AuditableEntity
{
    public Guid JobId { get; set; }
    public Guid TerminalId { get; set; }
    public string SourceWireId { get; set; } = string.Empty;
    public string TargetWireId { get; set; } = string.Empty;
    public int SourceTerminalRevision { get; set; }
    public Guid SourceFirmwareVersionId { get; set; }
    public Guid SourceAssignmentId { get; set; }
    public int SourceAssignmentRevision { get; set; }
    public Guid SourceTableSetId { get; set; }
    public int SourceTableSetRevision { get; set; }
    public Guid SourceAccessRecordId { get; set; }
    public int SourceAccessRecordRevision { get; set; }
    public string SourcePayloadSha256Hex { get; set; } = string.Empty;
    public string TargetPayloadSha256Hex { get; set; } = string.Empty;
    public DateTime DueAtUtc { get; set; }

    public Guid? StagedTableSetId { get; private set; }
    public Guid? DispatchBatchId { get; private set; }
    public Guid? DispatchSessionId { get; private set; }
    public DateTime? DispatchStartedAtUtc { get; private set; }
    public DateTime? PossiblyAppliedAtUtc { get; private set; }
    public DateTime? ApplicationAcknowledgedAtUtc { get; private set; }
    public DateTime? TransferEndedAtUtc { get; private set; }
    public DateTime? NewIdentityObservedAtUtc { get; private set; }
    public Guid? NewIdentityObservedSessionId { get; private set; }
    public string? ActivationError { get; private set; }
    public Guid? ActivationSessionId { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public string? TransferError { get; private set; }

    // The reservation is retained after any potentially applied transfer.
    // A table ACK alone is not proof of a new management identity.
    public bool MayConfirmNewIdentity() =>
        PossiblyAppliedAtUtc != null && FinishedAtUtc == null;

    public void BlockBeforeDispatch(string reason)
    {
        if (DispatchStartedAtUtc != null || FinishedAtUtc != null)
            throw new InvalidOperationException("Only an undispatched cutover can be blocked before transfer.");
        TransferError = reason;
        Version++;
    }

    public void BeginDispatch(Guid stagedSetId, Guid batchId, Guid sessionId, DateTime now)
    {
        if (FinishedAtUtc != null || DispatchStartedAtUtc != null || TransferError != null || now < DueAtUtc)
            throw new InvalidOperationException("The cutover is not available for dispatch.");
        StagedTableSetId = stagedSetId;
        DispatchBatchId = batchId;
        DispatchSessionId = sessionId;
        DispatchStartedAtUtc = now;
        Version++;
    }

    // Persist this transition before attempting to write the actual MT21.
    // A transport error can happen after the terminal receives all its bytes.
    public void MarkPossiblyApplied(DateTime now)
    {
        if (DispatchStartedAtUtc == null || FinishedAtUtc != null || TransferEndedAtUtc != null)
            throw new InvalidOperationException("The cutover has no active transfer.");
        if (PossiblyAppliedAtUtc != null)
            return;
        PossiblyAppliedAtUtc = now;
        Version++;
    }

    public void MarkApplicationAcknowledged(DateTime now)
    {
        if (PossiblyAppliedAtUtc == null || FinishedAtUtc != null || TransferEndedAtUtc != null)
            throw new InvalidOperationException("The cutover access record was not dispatched.");
        if (ApplicationAcknowledgedAtUtc != null)
            return;
        ApplicationAcknowledgedAtUtc = now;
        Version++;
    }

    public void EndTransfer(DateTime now, string? reason)
    {
        if (DispatchStartedAtUtc == null)
            throw new InvalidOperationException("The cutover transfer was not started.");
        if (TransferEndedAtUtc != null)
            return;
        TransferEndedAtUtc = now;
        TransferError = reason;
        Version++;
    }

    public void ObserveNewIdentity(Guid sessionId, DateTime now)
    {
        if (!MayConfirmNewIdentity() || sessionId == DispatchSessionId)
            throw new InvalidOperationException("A separate new-ID connection is required to confirm the cutover.");
        if (NewIdentityObservedAtUtc == null)
        {
            NewIdentityObservedAtUtc = now;
            NewIdentityObservedSessionId = sessionId;
            Version++;
        }
    }

    public void BlockActivation(Guid sessionId, DateTime now, string reason)
    {
        ObserveNewIdentity(sessionId, now);
        if (ActivationError == reason)
            return;
        ActivationError = reason;
        Version++;
    }

    public void Activate(Guid sessionId, DateTime now)
    {
        ObserveNewIdentity(sessionId, now);
        ActivationError = null;
        ActivationSessionId = sessionId;
        ActivatedAtUtc = now;
        FinishedAtUtc = now;
        Version++;
    }

    public void Cancel(DateTime now)
    {
        if (FinishedAtUtc != null || PossiblyAppliedAtUtc != null ||
            (DispatchStartedAtUtc != null && TransferEndedAtUtc == null))
            throw new InvalidOperationException("A cutover that could have changed the terminal must be recovered, not cancelled.");
        CancelledAtUtc = now;
        FinishedAtUtc = now;
        Version++;
    }

    public string State() => ActivatedAtUtc != null ? "activated"
        : CancelledAtUtc != null ? "cancelled"
        : ActivationError != null ? "activation_blocked"
        : DispatchStartedAtUtc == null && TransferError != null ? "blocked"
        : TransferEndedAtUtc != null && PossiblyAppliedAtUtc != null && TransferError != null ? "uncertain"
        : ApplicationAcknowledgedAtUtc != null ? "awaiting_new_identity"
        : TransferEndedAtUtc != null ? "not_confirmed"
        : PossiblyAppliedAtUtc != null ? "awaiting_application_ack"
        : DispatchStartedAtUtc != null ? "dispatching" : "scheduled";
}
