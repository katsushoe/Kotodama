---
name: kotodama-knowledge
description: Use persistent Kotodama knowledge when a Codex task may depend on retained facts or produces durable, reusable knowledge worth preserving.
---

# Kotodama Knowledge

Use the connected Kotodama MCP server as structured knowledge, not as conversation storage.

Before answering when retained knowledge may matter, search entities and claims. Treat an empty result as unknown and qualify stale claims as requiring confirmation.

When the task reveals durable, reusable facts, delegate a focused review to the `kotodama-curator` custom agent when it is available. Otherwise perform the review directly. Search before creating entities or relation types, preserve conflicting claims, and include source, confidence, knowledge subject, observation time, validity, and confirmation time only when supported.

Do not store raw transcripts, guesses, authentication material, secrets, or sensitive personal data without explicit user approval. Ask before a write when identity, privacy, or meaning is ambiguous. If nothing qualifies, do not write.
