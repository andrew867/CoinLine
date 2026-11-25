using HostPlatform.Domain;

namespace HostPlatform.Api.Hosting;

internal static class DownloadFailureDiagnostics
{
    public const string NotSentReason = "Not sent because the download stopped before this table.";

    public static bool WasNotSent(TableDownloadHostPhase phase) => phase is
        TableDownloadHostPhase.Draft or TableDownloadHostPhase.Validating or TableDownloadHostPhase.Ready
        or TableDownloadHostPhase.Queued or TableDownloadHostPhase.WaitingForTerminal;
}
