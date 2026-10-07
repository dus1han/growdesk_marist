using Microsoft.Extensions.Options;

namespace DoctorCrm.Api.Services;

/// <summary>
/// Keeps the subscription current without relying on webhooks reaching the server, and makes the
/// last automatic charge when a grace period runs out. Checks on start-up, then every few minutes;
/// does nothing while billing is switched off in Stripe Settings.
/// </summary>
public class BillingWorker(IServiceScopeFactory scopes, IOptions<BillingOptions> options, ILogger<BillingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.WorkerEnabled) return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(o.CheckMinutes, 1)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BillingService>().RunScheduledChecksAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Stripe unreachable or similar: the state stays as last synced; try again next tick.
                logger.LogError(ex, "Subscription check failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
