using Hotel.Application.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hotel.Infrastructure.Workers;

/// <summary>
/// Pushes ARI changes from the outbox and polls channels for reservations.
/// For multi-instance deployments run it on a single instance (ChannelSync:Enabled=false elsewhere)
/// or move to a distributed scheduler (Hangfire/Quartz with DB locks).
/// </summary>
public class ChannelSyncWorker(IServiceScopeFactory scopes, IOptions<ChannelSyncOptions> options, TimeProvider clock,
    ILogger<ChannelSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled) return;

        var lastPull = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, opt.AriPushIntervalSeconds)), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<ChannelSyncService>();
                await sync.ProcessAriOutboxAsync(stoppingToken);

                if (clock.GetUtcNow() - lastPull >= TimeSpan.FromSeconds(opt.ReservationPullIntervalSeconds))
                {
                    lastPull = clock.GetUtcNow();
                    await sync.PullAllAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Channel sync iteration failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
