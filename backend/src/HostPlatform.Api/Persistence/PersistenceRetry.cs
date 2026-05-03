using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Persistence;

public static class PersistenceRetry
{
    public static async Task<T> WithTransientRetryAsync<T>(
        Func<Task<T>> action,
        int maxAttempts,
        int delayMs,
        CancellationToken ct)
    {
        for (var attempt = 1;; attempt++)
        {
            try
            {
                return await action();
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                await Task.Delay(delayMs * attempt, ct);
            }
        }
    }

    public static async Task WithTransientRetryAsync(
        Func<Task> action,
        int maxAttempts,
        int delayMs,
        CancellationToken ct)
    {
        await WithTransientRetryAsync(async () =>
        {
            await action();
            return 0;
        }, maxAttempts, delayMs, ct);
    }
}
