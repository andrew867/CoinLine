namespace HostPlatform.Tests.Integration;

// An operator may read the previous snapshot while the bench is replacing it.
// Keep the replacement atomic, including when a Windows reader briefly denies delete sharing.
internal static class NativeBenchEvidenceWriter
{
    internal const int MaximumAttempts = 40;

    internal static void ReplaceWithRetry(string staging, string destination) =>
        ReplaceWithRetry(staging, destination, (source, target) => File.Move(source, target, overwrite: true),
            () => Thread.Sleep(50));

    internal static void ReplaceWithRetry(string staging, string destination, Action<string, string> replace, Action wait)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                replace(staging, destination);
                return;
            }
            catch (Exception error) when (attempt < MaximumAttempts &&
                                           error is IOException or UnauthorizedAccessException)
            {
                wait();
            }
        }
    }
}
