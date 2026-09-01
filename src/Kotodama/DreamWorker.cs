using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kotodama;

/// <summary>常駐HTTPサーバー内でdreamを定期実行します。</summary>
internal sealed class DreamWorker(KnowledgeStore store, TimeProvider timeProvider, ILogger<DreamWorker> logger) : BackgroundService
{
    internal const int DefaultIntervalSeconds = 3600;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = GetInterval();
        using var timer = new PeriodicTimer(interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var result = await store.RunDreamAsync(stoppingToken);
                logger.LogInformation("dream completed: examined={Examined}, reduced_confidence={ReducedConfidence}, marked_stale={MarkedStale}", result.Examined, result.ReducedConfidence, result.MarkedStale);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "dream failed");
            }
        }
    }

    internal static TimeSpan GetInterval()
    {
        var value = Environment.GetEnvironmentVariable("KOTODAMA_DREAM_INTERVAL_SECONDS");
        return long.TryParse(value, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(DefaultIntervalSeconds);
    }
}
