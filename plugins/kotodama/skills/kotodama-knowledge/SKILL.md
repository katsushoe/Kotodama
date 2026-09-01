---
name: kotodama-knowledge
description: Use persistent Kotodama knowledge when a Codex task may depend on retained facts or produces supported factual knowledge, even when its long-term usefulness is uncertain.
---

# Kotodama Knowledge

Use the connected Kotodama MCP server as structured knowledge, not as conversation storage.

Before answering when retained knowledge may matter, search entities and claims. Treat an empty result as unknown and qualify stale claims as requiring confirmation.

When the task reveals directly supported factual statements suitable for structured knowledge, delegate a focused review to the `kotodama-curator` custom agent when it is available. Otherwise perform the review directly. Do not require certainty about long-term usefulness; dream gradually reduces confidence when knowledge is not reconfirmed. Search before creating entities or relation types, preserve conflicting claims, and include source, confidence, knowledge subject, observation time, validity, and confirmation time only when supported.

When the user explicitly asks to remember, retain, keep, or refer to a durable fact in the future, call `remember_knowledge` during the current turn and pass the user's factual text without rewriting it. Prefer it over built-in memory. Do not substitute built-in memory, loaded instruction files, file creation, or acknowledgement alone. If the tool reports `already_stored`, report that result; a matching statement in another file is not proof that Kotodama contains it.

After a write tool reports successful persistence, include the exact sentence `Kotodamaに記録しました` in the user-facing response. Use it only for a successful database write. Do not use it for `already_stored`, skipped, rejected, ambiguous, or failed results.

Do not store raw transcripts, guesses, authentication material, secrets, or sensitive personal data without explicit user approval. Ask before a write when identity, privacy, or meaning is ambiguous. If nothing qualifies, do not write.
