# Kotodama

Local AI knowledge infrastructure combining a temporal, epistemic, weighted SQLite Knowledge Graph MCP server with Codex and Claude integrations.

Japanese documentation: [README.ja.md](README.ja.md)

Documentation index: [DOCUMENTS.ja.md](DOCUMENTS.ja.md)

## Overview

Kotodama combines a local SQLite-backed Knowledge Graph MCP server with client integrations that help AI agents safely register and retrieve durable knowledge from natural conversation. It provides a Codex plugin, skill, curator agent, and hooks; MCP configuration and hooks for Claude Code; and a DXT extension for Claude Desktop. The core server runs over stdio or Streamable HTTP and stores not only relationships between entities, but also who asserted them, their sources, confidence, validity periods, observation and confirmation times, and freshness. Conflicting positive and negative claims coexist instead of overwriting each other, and an absent claim means unknown rather than false.

Clients communicate with Kotodama over MCP stdio or opt-in Streamable HTTP to create entities and relation types, propose or retract claims, record events, and query knowledge by entity, relation type, or point in time. For remembered natural-language facts, `dream` reduces confidence to 80% after each unconfirmed 30-day period and marks the Claim `stale` below 0.2. Other expiring Claim types retain the existing direct stale transition. Dream never rewrites a Claim as false or physically deletes it.

## What an AI gains from Kotodama

After the Kotodama application or its supported plugin or extension is installed and configured for an AI client, the AI can use its tools to:

- retain structured knowledge across tasks and sessions instead of relying only on the current conversation;
- retrieve facts together with their source, confidence, knowledge subject, and valid time;
- keep contradictory reports side by side and reason about them without silently overwriting either one;
- distinguish "not known" from an explicitly negative claim;
- find related entities and reconstruct the knowledge context around a person, organization, object, or event;
- detect claims that need reconfirmation through `stale` status and `dream` processing.

For example, an AI can remember that a person belonged to an organization during a particular period, preserve both an official announcement and a conflicting report, and later answer with the applicable time and evidence. Kotodama provides storage and retrieval tools; the AI or MCP client must call those tools, and Kotodama does not automatically import conversations or update knowledge from the Internet.

Kotodama supplies server instructions during MCP initialization and exposes the `use_kotodama` MCP prompt. Explicit requests such as "remember this" can be persisted in one call with the `remember_knowledge` tool, which is preferred over built-in memory by the server instructions. `configure claude` and `configure codex` also install client hooks that prompt knowledge retrieval before an answer and knowledge review after it. Raw transcripts are never stored. Directly supported facts may be selected even when their long-term usefulness is uncertain; dream gradually reduces confidence when they are not reconfirmed.

For Codex, `plugins/kotodama` provides a plugin containing the MCP connection and the `kotodama-knowledge` skill. `configure codex` also installs the user-scoped `kotodama-curator` custom agent so post-response knowledge review can run in an isolated context. The parent agent performs the same review when the custom agent is unavailable.

After a new database write succeeds, the AI reports the exact sentence `Kotodamaに記録しました` to the user. It does not use this success notice for an existing duplicate, skipped write, pending confirmation, rejection, or failure.

## Data model

```text
Entity --< directed/symmetric Relation >-- Entity
                         |
                         +-- Claim -- optional --> Source
                               |
                               +-- optional knowledge subject (Entity)

Entity -- optional specialization --> Event
```

- **Entity** identifies a person, organization, object, concept, or event.
- **RelationType** defines a relation's meaning, directionality, optional strength, and freshness policy.
- **Relation** is the normalized directed or symmetric edge between two entities.
- **Claim** is an assertion about a relation. It carries polarity, confidence, optional attribution confidence and strength, temporal fields, knowledge subject, source, and status.
- **Source** describes evidence such as a document, statement, or URL. It is distinct from the entity that knows or asserts the claim.
- **Event** is an entity with an occurrence time, actor, action, and object or value.

Claim status transitions are `active -> retracted` by explicit retraction and `active -> stale` by `dream`. `stale` means that currentness needs confirmation; it does not mean false. Validity uses a half-open interval: `valid_from` is inclusive and `valid_to` is exclusive. See [Knowledge Model](KNOWLEDGE_MODEL.ja.md) for details.

## Run

```powershell
$env:KOTODAMA_DB = "C:\data\kotodama.db"
dotnet run --project src/Kotodama
```

When `KOTODAMA_DB` is omitted, an installed copy creates `kotodama.db` in its `data` directory. Other layouts create it next to the executable. The server uses MCP stdio; logs are written to stderr.

`KOTODAMA_DREAM_TEMP_STORE` selects the dream staging location: `Default`, `Memory`, or `File`. Dream calculates eligible Claim states in a connection-local temporary table, then publishes stale transitions atomically in a short transaction.

To run the stateless Streamable HTTP transport on loopback:

```powershell
$env:KOTODAMA_TRANSPORT = "http"
$env:KOTODAMA_HTTP_URL = "http://127.0.0.1:39280"
$env:KOTODAMA_HTTP_TOKEN = "a sufficiently long random token"
dotnet run --project src/Kotodama
```

Connect the MCP client to `http://127.0.0.1:39280/mcp`. When `KOTODAMA_HTTP_TOKEN` is set, the client must send the same value as a Bearer token. HTTP mode remains restricted to loopback and does not provide per-user authorization. See [configuration](CONFIG.ja.md).

## MCP tools

`get_version`, `get_entity`, `search_entities`, `create_entity`, `create_relation_type`, `create_event`, `query_relations`, `query_claims`, `get_neighbors`, `get_knowledge_context`, `propose_claim`, `remember_knowledge`, `retract_claim`, and `run_dream`.

Administrative tools also support claim reactivation and explicit physical deletion, plus RelationType update and deletion. RelationTypes that are still referenced are not deleted. In HTTP mode, dream runs periodically (3600 seconds by default), daily logs are written under the deployment `logs` directory, and `kotodama backup <destination.db>` creates an online SQLite backup.

The storage model preserves conflicting positive and negative claims, distinguishes Source from knowledge subject, normalizes symmetric edges, supports temporal querying, and gradually reduces unconfirmed remembered knowledge before excluding it as `stale` from default queries.

## Installation

### MSI installer

Download [Kotodama-0.11.5-x64.msi](https://github.com/katsushoe/Kotodama/releases/download/v0.11.5/Kotodama-0.11.5-x64.msi), verify its SHA-256, and run it with administrator privileges:

```powershell
Get-FileHash .\Kotodama-0.11.5-x64.msi -Algorithm SHA256
Start-Process msiexec.exe -Verb RunAs -Wait `
  -ArgumentList '/i "Kotodama-0.11.5-x64.msi" /norestart'
```

The x64 MSI installs Kotodama under `C:\Kotodama`:

- `bin`: executable and runtime files
- `config`: local configuration
- `data`: SQLite databases and application data
- `logs`: logs

Configuration, databases, and logs are not included in the MSI. Non-empty data directories remain when upgrading or uninstalling. Set `KOTODAMA_DB` when the database should be stored outside the executable directory.

### Portable ZIP

Download [Kotodama-0.11.5-win-x64.zip](https://github.com/katsushoe/Kotodama/releases/download/v0.11.5/Kotodama-0.11.5-win-x64.zip), verify its SHA-256, and extract it to a writable directory:

```powershell
Get-FileHash .\Kotodama-0.11.5-win-x64.zip -Algorithm SHA256
Expand-Archive .\Kotodama-0.11.5-win-x64.zip -DestinationPath C:\Tools
& C:\Tools\Kotodama\bin\Kotodama.exe
```

The ZIP is self-contained and does not require a separately installed .NET Runtime. It does not register Kotodama with Windows, modify `PATH`, or provide automatic upgrades. Preserve the extracted `data` directory when replacing a version.

### Claude Desktop Extension

Download `Kotodama-<version>-win-x64.dxt`, then open Claude Desktop and select `Settings > Extensions > Advanced settings > Install Extension...`. Choose a data directory when prompted. Claude Desktop launches the bundled self-contained Kotodama binary as a stdio MCP server.

The extension exposes Kotodama tools, server instructions, and the `use_kotodama` prompt. Regular Claude Desktop does not run the Claude Code hooks or subagents, so automatic capture after every response is not guaranteed. Select the `use_kotodama` prompt when explicit knowledge review is required. Removing the extension does not delete the selected data directory.

### Source distribution

Install the .NET 10 SDK, then clone and build the source:

```powershell
git clone https://github.com/katsushoe/Kotodama.git
Set-Location Kotodama
git checkout v0.11.5
dotnet restore Kotodama.slnx
dotnet build Kotodama.slnx -c Release --no-restore
$env:KOTODAMA_DB = (Join-Path $PWD 'data\kotodama.db')
New-Item -ItemType Directory -Force data | Out-Null
dotnet run --project src\Kotodama -c Release --no-build
```

Kotodama uses MCP stdio by default, so a direct launch waits for JSON-RPC input. Set the HTTP environment variables above to run it as a Streamable HTTP server instead.
