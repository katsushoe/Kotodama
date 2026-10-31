using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kotodama;

/// <summary>常駐HTTPサーバー内でdreamを定期実行します。</summary>
internal sealed class DreamWorker(KnowledgeStore store, TimeProvider timeProvider, KotodamaSettings settings, ILogger<DreamWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.GetDreamInterval(), timeProvider);
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
}
