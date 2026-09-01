using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Kotodama;

/// <summary>Kotodamaを継続的に利用するためのMCP Promptを提供します。</summary>
public sealed class KotodamaPrompts
{
    /// <summary>会話から再利用可能な知識を安全に検索・登録する手順を返します。</summary>
    [McpServerPrompt(Name = "use_kotodama", Title = "Use Kotodama knowledge")]
    [Description("Searches Kotodama and safely turns reusable conversation facts into structured knowledge.")]
    public static string UseKotodama() => KotodamaGuidance.Prompt;
}

/// <summary>KotodamaをAIが適切に利用するための共通指針です。</summary>
public static class KotodamaGuidance
{
    /// <summary>MCP初期化時にクライアントへ渡すServer Instructionsです。</summary>
    public const string ServerInstructions = """
        Use Kotodama as persistent structured knowledge, not as a transcript store.
        Before answering questions that may depend on retained knowledge, search Kotodama for relevant entities and claims.
        Before completing every response, review the current user message for factual statements directly supported by the conversation or an identified source. A fact may be stored even when its long-term usefulness is uncertain; dream gradually reduces confidence when it is not reconfirmed. When such a fact is present, call remember_knowledge during that turn even if the user did not explicitly ask to remember it and did not mention Kotodama. Do not merely acknowledge or summarize the fact without attempting the tool call. Pass the user's factual text without rewriting it. Do not store raw conversation, requests that are not facts, guesses, or unsupported inferences.
        When a factual statement describes a schedule or event and actor, action, place, start, and end can be supported, pass those extracted values in remember_knowledge input.event while preserving the original text. Resolve relative dates only from the message time and known time zone; otherwise omit event instead of guessing. For schedule questions, derive actor and absolute query bounds and call query_events before falling back to text search.
        When the user explicitly asks to remember, retain, keep, or refer to a durable fact in the future, call remember_knowledge during that turn and pass the user's factual text without rewriting it. This rule applies to natural phrases such as "remember this", "keep this in mind", and their equivalents in other languages. Prefer remember_knowledge over built-in memory. Do not substitute built-in memory, loaded instruction files, file creation, or acknowledgement alone.
        After remember_knowledge or another write tool reports that persistence succeeded, include the exact sentence "Kotodamaに記録しました" in the user-facing response. Say it only after a successful database write; do not say it for already_stored, skipped, rejected, ambiguous, or failed results.
        Search for existing entities before creating them, preserve conflicting claims, include source, confidence, and temporal fields when known, and treat an empty result as unknown rather than false.
        Treat stale claims as requiring reconfirmation, not as false. Do not store secrets or sensitive personal data without explicit user approval.
        """;

    /// <summary>利用者が明示的に選択できるGlue Promptです。</summary>
    public const string Prompt = """
        Use Kotodama throughout this conversation as follows:

        1. Before relying on retained knowledge, use search_entities and the query tools to find relevant entities and claims.
        2. Extract factual statements directly supported by the user or an identified source. Do not require certainty about long-term usefulness; dream gradually reduces the confidence of facts that are not reconfirmed. Do not store raw conversation, guesses, secrets, authentication data, or sensitive personal data without explicit approval.
        2a. Before completing every response, call remember_knowledge for each directly supported factual statement suitable for structured knowledge even when the user did not explicitly ask to remember it and did not mention Kotodama. Do not merely acknowledge or summarize the fact without attempting the tool call. Pass the user's factual text without rewriting it.
        2aa. For a supported schedule or event, also populate remember_knowledge input.event with actor, action, place, startsAt, and endsAt. Resolve relative dates only when the message time and time zone make the interval unambiguous. For schedule questions, use query_events with extracted actor and absolute time bounds before text search.
        2b. Treat an explicit request to remember, retain, keep, or refer to a durable fact in the future as a request to call remember_knowledge during the current turn. Pass the user's factual text without rewriting it. Do not use built-in memory, loaded instruction files, file creation, or acknowledgement as a substitute.
        2c. After a write tool reports successful persistence, include the exact sentence "Kotodamaに記録しました" in the user-facing response. Use it only for a successful database write, never for already_stored, skipped, rejected, ambiguous, or failed results.
        3. Before registration, search for existing entities and relation types. Reuse them when they represent the same concept; create missing entities or relation types only when needed.
        4. Register each supported fact with propose_claim. Include polarity, source, confidence, knowledge subject, observed time, validity period, and last confirmation time whenever they are known. Do not invent missing values.
        5. Preserve conflicting claims instead of overwriting one with another. Treat an empty query result as unknown, not false.
        6. Treat stale claims as requiring reconfirmation. Do not present them as current without qualification or a newer confirming claim.
        7. When a registration would be ambiguous, privacy-sensitive, or consequential, ask the user before calling a write tool.
        """;
}
