using System.Text.Json;

namespace Kotodama;

/// <summary>Claude CodeのHookイベントへKotodama利用指針を返します。</summary>
internal static class ClaudeHookCommand
{
    internal const string IntegrationId = "kotodama";

    private const string SearchContext = """
        Use the connected Kotodama MCP server before answering when retained knowledge may be relevant. Search first, treat an empty result as unknown, and do not store secrets or raw conversation transcripts.
        """;

    private const string ExplicitPersistenceContext = """
        The user explicitly asked to remember durable knowledge. Treat this as an explicit request to persist the fact in the connected Kotodama MCP server during this turn. Do not satisfy the request with built-in memory, loaded instruction files, or a verbal acknowledgement alone. Search Kotodama first. If an equivalent active claim already exists, report that it is already stored; otherwise create or reuse the required entities and relation type, then call propose_claim. After a write tool reports successful persistence, include the exact sentence "Kotodamaに記録しました" in the user-facing response. Say it only after a successful database write, not for already_stored or any skipped, rejected, ambiguous, or failed result. A matching fact in AGENTS.md or another loaded file does not mean it is already stored in Kotodama. Keep the existing restrictions on secrets, authentication data, sensitive personal data, ambiguity, guesses, and raw transcripts.
        """;

    private const string PersistReason = """
        Before finishing every completed turn, review the user message and your response for factual statements suitable for structured knowledge even when the user did not explicitly ask to remember them. Do not require certainty about long-term usefulness; dream gradually reduces confidence when knowledge is not reconfirmed. For Codex, delegate this focused review to the kotodama-curator custom agent when available; otherwise review directly. Use the connected Kotodama MCP tools, search for existing entities and claims first, and store only facts directly supported by the user's factual text or an identified source. Treat assistant-generated text without an identified source as a candidate to discard, not as evidence. When one user statement can be stored through remember_knowledge, pass only that factual statement without rewriting it; never pass or store the raw transcript. After a write tool reports successful persistence, include the exact sentence "Kotodamaに記録しました" in the user-facing response. Say it only after a successful database write, not for already_stored or any skipped, rejected, ambiguous, or failed result. If the user explicitly asked to remember, retain, or keep a fact for future use, use Kotodama now rather than built-in memory, loaded instruction files, or acknowledgement alone. Preserve conflicts, include source and temporal metadata only when known, and never store secrets, authentication data, sensitive personal data without explicit approval, guesses, or unsupported inferences. If an equivalent non-retracted claim already exists, reconfirm it instead of creating a duplicate. If there is nothing suitable to store, finish without writing.
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
                    additionalContext = BuildPromptContext(document.RootElement),
                },
            },
            "stop" => BuildStopResult(clientName, document.RootElement),
            _ => throw new ArgumentException($"Unsupported Claude hook event: {eventName}", nameof(eventName)),
        };

        await output.WriteLineAsync(JsonSerializer.Serialize(result));
        return 0;
    }

    private static string BuildPromptContext(JsonElement input)
    {
        var prompt = input.TryGetProperty("prompt", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return HasExplicitPersistenceIntent(prompt)
            ? SearchContext + Environment.NewLine + ExplicitPersistenceContext
            : SearchContext;
    }

    private static bool HasExplicitPersistenceIntent(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        if (prompt.Contains("覚えて", StringComparison.Ordinal) ||
            prompt.Contains("記憶しておいて", StringComparison.Ordinal) ||
            prompt.Contains("今後もこの方針", StringComparison.Ordinal))
        {
            return true;
        }

        return prompt.Contains("remember this", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("remember that", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("keep this in mind", StringComparison.OrdinalIgnoreCase);
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
