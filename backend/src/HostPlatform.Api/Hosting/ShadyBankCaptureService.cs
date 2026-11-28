namespace HostPlatform.Api.Hosting;

/// <summary>External posting runs independently of the NCC receipt and transport timing.</summary>
internal sealed class ShadyBankCaptureService(IServiceScopeFactory scopes, ILogger<ShadyBankCaptureService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<ShadyBankCaptureCoordinator>();
                await coordinator.PrepareRecentAsync(stoppingToken);
                await coordinator.DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                // Provider failures and database diagnostics may contain sensitive material.
                logger.LogWarning("Scoped merchant capture pass failed ({ErrorType}).", ex.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
