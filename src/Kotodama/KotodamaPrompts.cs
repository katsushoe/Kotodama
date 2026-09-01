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
        When the conversation contains a durable, reusable fact, consider storing it only when it is directly supported by the conversation or an identified source.
        When the user explicitly asks to remember, retain, keep, or refer to a durable fact in the future, call remember_knowledge during that turn and pass the user's factual text without rewriting it. This rule applies to natural phrases such as "remember this", "keep this in mind", and their equivalents in other languages. Prefer remember_knowledge over built-in memory. Do not substitute built-in memory, loaded instruction files, file creation, or acknowledgement alone.
        Search for existing entities before creating them, preserve conflicting claims, include source, confidence, and temporal fields when known, and treat an empty result as unknown rather than false.
        Treat stale claims as requiring reconfirmation, not as false. Do not store secrets or sensitive personal data without explicit user approval.
        """;

    /// <summary>利用者が明示的に選択できるGlue Promptです。</summary>
    public const string Prompt = """
        Use Kotodama throughout this conversation as follows:

        1. Before relying on retained knowledge, use search_entities and the query tools to find relevant entities and claims.
        2. Extract only durable, reusable facts that are directly supported by the user or an identified source. Do not store casual conversation, guesses, secrets, authentication data, or sensitive personal data without explicit approval.
        2a. Treat an explicit request to remember, retain, keep, or refer to a durable fact in the future as a request to call remember_knowledge during the current turn. Pass the user's factual text without rewriting it. Do not use built-in memory, loaded instruction files, file creation, or acknowledgement as a substitute.
        3. Before registration, search for existing entities and relation types. Reuse them when they represent the same concept; create missing entities or relation types only when needed.
        4. Register each supported fact with propose_claim. Include polarity, source, confidence, knowledge subject, observed time, validity period, and last confirmation time whenever they are known. Do not invent missing values.
        5. Preserve conflicting claims instead of overwriting one with another. Treat an empty query result as unknown, not false.
        6. Treat stale claims as requiring reconfirmation. Do not present them as current without qualification or a newer confirming claim.
        7. When a registration would be ambiguous, privacy-sensitive, or consequential, ask the user before calling a write tool.
        """;
}
