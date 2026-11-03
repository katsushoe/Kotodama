using Microsoft.Data.Sqlite;

namespace Kotodama;

public sealed partial class KnowledgeStore
{
    /// <summary>MCP Toolの利用メタデータだけを記録します。</summary>
    internal async Task RecordMcpToolUsageAsync(string toolName, DateTimeOffset startedAt, long durationMilliseconds, string outcome, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        if (durationMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        if (outcome is not ("success" or "error" or "cancelled")) throw new ArgumentOutOfRangeException(nameof(outcome));

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO mcp_tool_usage_log(tool_name,started_at,duration_ms,outcome) VALUES($tool,$started,$duration,$outcome)";
        command.Parameters.AddWithValue("$tool", toolName);
        command.Parameters.AddWithValue("$started", Format(startedAt));
        command.Parameters.AddWithValue("$duration", durationMilliseconds);
        command.Parameters.AddWithValue("$outcome", outcome);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
