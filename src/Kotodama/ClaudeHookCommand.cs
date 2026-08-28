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

    private const string CodexStatePrefix = "kotodama-codex-stop-";

    internal static async Task<int> RunAsync(string clientName, string eventName, TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
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
            "stop" => BuildStopResult(clientName, document.RootElement),
            _ => throw new ArgumentException($"Unsupported Claude hook event: {eventName}", nameof(eventName)),
        };

        await output.WriteLineAsync(JsonSerializer.Serialize(result));
        return 0;
    }

    private static object BuildStopResult(string clientName, JsonElement input)
    {
        if (clientName.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCodexStopResult(input);
        }

        var alreadyActive = input.TryGetProperty("stop_hook_active", out var value) && value.ValueKind == JsonValueKind.True;
        return alreadyActive ? new { } : new { decision = "block", reason = PersistReason };
    }

    private static object BuildCodexStopResult(JsonElement input)
    {
        var sessionId = input.TryGetProperty("session_id", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(sessionId)) return new { };

        var safeId = string.Concat(sessionId.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        var statePath = Path.Combine(Path.GetTempPath(), CodexStatePrefix + safeId);
        if (File.Exists(statePath))
        {
            File.Delete(statePath);
            return new { };
        }

        File.WriteAllText(statePath, string.Empty);
        return new { decision = "block", reason = PersistReason };
    }
}
