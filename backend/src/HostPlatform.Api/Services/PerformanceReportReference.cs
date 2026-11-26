using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Api.Services;

internal sealed record PerformanceReportReference(
    string Status,
    Guid? DownloadBatchId = null,
    int? TableRevision = null,
    DateTime? AckRecordedAtUtc = null,
    bool LaterUncertainAttempt = false,
    IReadOnlyList<PerformanceTimingPair>? Timing = null);

internal static class PerformanceReportReferences
{
    public static async Task<PerformanceReportReference> FindAsync(HostPlatformDbContext db,
        Guid? terminalId, DateTime receivedAtUtc, CancellationToken ct)
    {
        var source = await AcknowledgedConfigurationReferences.FindAsync(db, terminalId, receivedAtUtc,
            CollectionPolicyWire.CommunicationsType, ct);
        if (source.Status != "acknowledged") return new(source.Status);
        var timing = CollectionPolicyWire.ReadPerformanceTiming(source.Payload!);
        return new(timing == null ? "invalid" : "acknowledged", source.DownloadBatchId,
            source.TableRevision, source.AckRecordedAtUtc, source.LaterUncertainAttempt, timing);
    }
}
