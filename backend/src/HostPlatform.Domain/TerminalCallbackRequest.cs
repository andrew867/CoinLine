namespace HostPlatform.Domain;

public sealed class TerminalCallbackRequest : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public int TerminalClockOffsetMinutes { get; set; }
    public bool DeliverAssignedTables { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public DateTime? ProgrammedForUtc { get; set; }
    public Guid? AcknowledgedSessionId { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
}
