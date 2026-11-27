namespace HostPlatform.Protocols.Tables;

/// <summary>Read-only review of collection controls contained in a configuration, not the installed phone's state.</summary>
public sealed record CollectionConfigurationReview(
    string StatisticsRecordStatus, string CallInRecordStatus,
    int? StatisticsIntervalDays, int? StatisticsUploadThreshold, bool? CarrierStatisticsEnabled,
    string? OptionalCdrMode, int? OptionalCdrModeMask, int? OptionalCdrUploadThreshold,
    string? OptionalWindowStart, int? OptionalWindowDays, int? OptionalWindowHours,
    int? NormalCdrUploadThreshold, IReadOnlyList<TableReviewFinding> Findings)
{
    public static CollectionConfigurationReview Build(IReadOnlyDictionary<int, byte[]> records)
    {
        var hasStatistics = records.TryGetValue(CollectionPolicyWire.CallCarrierType, out var statisticsRecord);
        var hasCallIn = records.TryGetValue(NativeTableLayouts.CallInRecord, out var callInRecord);
        var statistics = hasStatistics ? CollectionPolicyWire.ReadCallCarrierSettings(statisticsRecord!) : null;
        var normalThreshold = hasCallIn ? CollectionPolicyWire.ReadCallInRecordThreshold(callInRecord!) : null;
        var statisticsStatus = statistics != null ? "configured" : hasStatistics ? "unrecognized" : "missing";
        var callInStatus = normalThreshold != null ? "configured" : hasCallIn ? "unrecognized" : "missing";
        var findings = new List<TableReviewFinding>();

        if (statistics == null)
            findings.Add(new(34, hasStatistics
                ? "The included MT34 does not match the recognized collection layout. Do not infer a logging or statistics policy from its presence."
                : "MT34 is not included. This set does not establish the phone's optional CDR logging window or carrier statistics policy."));
        if (normalThreshold == null)
            findings.Add(new(35, hasCallIn
                ? "The included MT35 does not match the recognized call-in layout. Its normal call-record threshold is unknown."
                : "MT35 is not included. This set does not establish the normal call-record upload threshold outside the optional logging window."));
        else if (normalThreshold == 0)
            findings.Add(new(35, "No positive normal call-record upload threshold is configured. Review the intended routine and manual collection policy."));

        int? mask = statistics == null ? null : statistics.PolicyFlags & CollectionPolicyWire.OptionalCdrMask;
        var mode = mask switch
        {
            null => null, 0 => "Normal calls only (optional logging disabled)", 1 => "All call attempts", 4 => "Completed calls only",
            8 => "Completed calls except local coin", 16 => "Completed calls except local coin and stored-value", _ => "Unrecognized combination"
        };
        string? windowStart = null;
        if (statistics != null)
        {
            if (statistics.DetailHour <= 23 && statistics.DetailMinute <= 59)
                windowStart = $"{statistics.DetailHour:D2}:{statistics.DetailMinute:D2}";
            if (mask != 0)
            {
                if (mask is not (1 or 4 or 8 or 16))
                    findings.Add(new(34, "Several optional logging mode bits are set. Review the imported policy before relying on a single call filter."));
                if (windowStart == null || statistics.DetailHours > 23)
                    findings.Add(new(34, "The optional logging window contains an unrecognized start or duration. Review the saved MT34 fields."));
                if (statistics.DetailDays == 0 && statistics.DetailHours == 0)
                    findings.Add(new(34, "The stored zero optional logging duration is interpreted as one day by the terminal."));
                if (statistics.DetailThreshold == 0)
                    findings.Add(new(34, "No positive optional CDR upload threshold is configured. This does not establish whether a call is logged or when it is uploaded."));
                if (mask is 8 or 16)
                    findings.Add(new(34, "The selected optional mode excludes local coin calls. A local coin test is not a valid proof of this logging policy."));
            }
            if (statistics.Days == 0 && (mask != 0 || (statistics.PolicyFlags & 2) != 0))
                findings.Add(new(34, "The zero statistics collection interval disables call statistics. Review the legacy requirement for statistics to be enabled with optional CDR or carrier collection."));
        }

        return new(statisticsStatus, callInStatus, statistics?.Days, statistics?.Threshold,
            statistics == null ? null : (statistics.PolicyFlags & 2) != 0,
            mode, mask, statistics?.DetailThreshold, windowStart, statistics?.DetailDays, statistics?.DetailHours,
            normalThreshold, findings);
    }
}
