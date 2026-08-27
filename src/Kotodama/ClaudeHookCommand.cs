using System.Text.Json;

namespace Kotodama;

/// <summary>Claude CodeのHookイベントへKotodama利用指針を返します。</summary>
internal static class ClaudeHookCommand
{
    internal const string IntegrationId = "kotodama";

    private const string SearchContext = """
        Use the connected Kotodama MCP server before answering when retained knowledge may be relevant. Search first, treat an empty result as unknown, and do not store secrets or raw conversation transcripts.
        """;

    private const string PersistReason = """
        Before finishing this turn, review the user message and your response for durable, reusable facts. Use the connected Kotodama MCP tools to search for existing entities and claims, then store only directly supported facts. Preserve conflicts, include source and temporal metadata when known, and never store secrets, authentication data, sensitive personal data without explicit approval, guesses, or the raw transcript. If there is nothing suitable to store, finish without writing.
        """;

    internal static async Task<int> RunAsync(string eventName, TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        using var document = await JsonDocument.ParseAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(await input.ReadToEndAsync(cancellationToken))),
            cancellationToken: cancellationToken);

        object result = eventName.ToLowerInvariant() switch
        {
            "user-prompt-submit" => new
            {
                hookSpecificOutput = new
                {
                    hookEventName = "UserPromptSubmit",
                    additionalContext = SearchContext,
                },
            },
            "stop" => BuildStopResult(document.RootElement),
            _ => throw new ArgumentException($"Unsupported Claude hook event: {eventName}", nameof(eventName)),
        };

        await output.WriteLineAsync(JsonSerializer.Serialize(result));
        return 0;
    }

    private static object BuildStopResult(JsonElement input)
    {
        var alreadyActive = input.TryGetProperty("stop_hook_active", out var value) && value.ValueKind == JsonValueKind.True;
        return alreadyActive ? new { } : new { decision = "block", reason = PersistReason };
    }
}
