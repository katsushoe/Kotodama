---
name: kotodama-knowledge
description: Use persistent Kotodama knowledge when a Codex task may depend on retained facts or produces durable, reusable knowledge worth preserving.
---

# Kotodama Knowledge

Use the connected Kotodama MCP server as structured knowledge, not as conversation storage.

Before answering when retained knowledge may matter, search entities and claims. Treat an empty result as unknown and qualify stale claims as requiring confirmation.

When the task reveals durable, reusable facts, delegate a focused review to the `kotodama-curator` custom agent when it is available. Otherwise perform the review directly. Search before creating entities or relation types, preserve conflicting claims, and include source, confidence, knowledge subject, observation time, validity, and confirmation time only when supported.

When the user explicitly asks to remember, retain, keep, or refer to a durable fact in the future, call `remember_knowledge` during the current turn and pass the user's factual text without rewriting it. Prefer it over built-in memory. Do not substitute built-in memory, loaded instruction files, file creation, or acknowledgement alone. If the tool reports `already_stored`, report that result; a matching statement in another file is not proof that Kotodama contains it.

Do not store raw transcripts, guesses, authentication material, secrets, or sensitive personal data without explicit user approval. Ask before a write when identity, privacy, or meaning is ambiguous. If nothing qualifies, do not write.
