using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kotodama;

internal static class McpToolUsageLogging
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Wrap(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.Services is not { } services)
            {
                return await next(context, cancellationToken).ConfigureAwait(false);
            }

            var timeProvider = services.GetRequiredService<TimeProvider>();
            var recorder = services.GetRequiredService<McpToolUsageRecorder>();
            var startedAt = timeProvider.GetUtcNow();
            var startedTimestamp = timeProvider.GetTimestamp();
            var outcome = "success";

            try
            {
                var result = await next(context, cancellationToken).ConfigureAwait(false);
                if (result.IsError == true) outcome = "error";
                return result;
            }
            catch (OperationCanceledException)
            {
                outcome = "cancelled";
                throw;
            }
            catch
            {
                outcome = "error";
                throw;
            }
            finally
            {
                var durationMilliseconds = (long)Math.Max(0, timeProvider.GetElapsedTime(startedTimestamp).TotalMilliseconds);
                await recorder.RecordBestEffortAsync(context.Params.Name, startedAt, durationMilliseconds, outcome).ConfigureAwait(false);
            }
        };
}

internal sealed class McpToolUsageRecorder(KnowledgeStore store, ILogger<McpToolUsageRecorder> logger)
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);

    public async Task RecordBestEffortAsync(string toolName, DateTimeOffset startedAt, long durationMilliseconds, string outcome)
    {
        using var timeout = new CancellationTokenSource(WriteTimeout);
        try
        {
            await store.RecordMcpToolUsageAsync(toolName, startedAt, durationMilliseconds, outcome, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Telemetry must not change the tool result or disclose request parameters.
            logger.LogWarning(exception, "[McpToolUsage] Could not record usage for tool {ToolName}", toolName);
        }
    }
}
