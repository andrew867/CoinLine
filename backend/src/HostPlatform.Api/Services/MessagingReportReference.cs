using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Api.Services;

internal sealed record MessagingReportReference(string Status, Guid? DownloadBatchId = null,
    int? TableRevision = null, DateTime? AckRecordedAtUtc = null, bool LaterUncertainAttempt = false,
    MessagingReportSettings? Settings = null);

internal static class MessagingReportReferences
{
    public static async Task<MessagingReportReference> FindAsync(HostPlatformDbContext db,
        Guid? terminalId, DateTime receivedAtUtc, CancellationToken ct)
    {
        var source = await AcknowledgedConfigurationReferences.FindAsync(
            db, terminalId, receivedAtUtc, MessagingServiceWire.MessageType, ct);
        if (source.Status != "acknowledged") return new(source.Status);
        var settings = MessagingServiceWire.ReadReportSettings(source.Payload!);
        return new(settings == null ? "invalid" : "acknowledged", source.DownloadBatchId,
            source.TableRevision, source.AckRecordedAtUtc, source.LaterUncertainAttempt, settings);
    }
}
