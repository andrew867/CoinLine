using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Api.Services;

internal sealed record CollectionReportReference(string Status, int RecordNumber, Guid? DownloadBatchId = null,
    int? TableRevision = null, DateTime? AckRecordedAtUtc = null, bool LaterUncertainAttempt = false,
    CallCarrierCollectionSettings? CollectionSettings = null, int? CallRecordThreshold = null);

internal static class CollectionReportReferences
{
    public static async Task<CollectionReportReference> FindAsync(HostPlatformDbContext db, Guid? terminalId,
        DateTime receivedAtUtc, int recordNumber, CancellationToken ct)
    {
        var source = await AcknowledgedConfigurationReferences.FindAsync(db, terminalId, receivedAtUtc, recordNumber, ct);
        if (source.Status != "acknowledged") return new(source.Status, recordNumber);
        var collectionSettings = recordNumber == CollectionPolicyWire.CallCarrierType
            ? CollectionPolicyWire.ReadCallCarrierSettings(source.Payload!) : null;
        var callRecordThreshold = recordNumber == NativeTableLayouts.CallInRecord
            ? CollectionPolicyWire.ReadCallInRecordThreshold(source.Payload!) : null;
        return new(collectionSettings != null || callRecordThreshold.HasValue ? "acknowledged" : "invalid", recordNumber,
            source.DownloadBatchId, source.TableRevision, source.AckRecordedAtUtc, source.LaterUncertainAttempt,
            collectionSettings, callRecordThreshold);
    }
}
