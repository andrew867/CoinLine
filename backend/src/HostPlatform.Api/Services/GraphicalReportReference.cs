using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Api.Services;

internal sealed record GraphicalReportReference(string Status, int RecordNumber, Guid? DownloadBatchId = null,
    int? TableRevision = null, DateTime? AckRecordedAtUtc = null, bool LaterUncertainAttempt = false,
    ushort? CollectionDurationDays = null, IReadOnlyList<GraphicalUsageScreen>? Screens = null);

internal static class GraphicalReportReferences
{
    private static readonly int[] RecordNumbers = [131, 132, 133];

    public static IReadOnlyList<GraphicalReportReference> Restricted() =>
        RecordNumbers.Select(number => new GraphicalReportReference("restricted", number)).ToArray();

    public static async Task<IReadOnlyList<GraphicalReportReference>> FindAsync(HostPlatformDbContext db,
        Guid? terminalId, DateTime receivedAtUtc, CancellationToken ct)
    {
        var results = new List<GraphicalReportReference>();
        foreach (var number in RecordNumbers)
        {
            var source = await AcknowledgedConfigurationReferences.FindAsync(db, terminalId, receivedAtUtc, number, ct);
            if (source.Status != "acknowledged")
            {
                results.Add(new(source.Status, number));
                continue;
            }
            var review = NativeTableLayouts.ReviewGraphicalLinks(new Dictionary<int, byte[]> { [number] = source.Payload! });
            var valid = review.Records.Single(record => record.Number == number).Availability == "included";
            results.Add(new(valid ? "acknowledged" : "invalid", number, source.DownloadBatchId, source.TableRevision,
                source.AckRecordedAtUtc, source.LaterUncertainAttempt,
                valid ? review.Usage.CollectionDurationDays : null, valid ? review.Usage.Screens : null));
        }
        return results;
    }
}
