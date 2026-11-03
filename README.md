# Kotodama

Local AI knowledge infrastructure combining a temporal, epistemic, weighted SQLite Knowledge Graph MCP server with Codex and Claude integrations.

Japanese documentation: [README.ja.md](README.ja.md)

Documentation index: [DOCUMENTS.ja.md](DOCUMENTS.ja.md)

## Overview

Kotodama combines a local SQLite-backed Knowledge Graph MCP server with client integrations that help AI agents safely register and retrieve durable knowledge from natural conversation. It provides a Codex plugin, skill, curator agent, and hooks; and MCP configuration and hooks for Claude Code. The core server runs as a resident Streamable HTTP service and stores not only relationships between entities, but also who asserted them, their sources, confidence, validity periods, observation and confirmation times, and freshness. Conflicting positive and negative claims coexist instead of overwriting each other, and an absent claim means unknown rather than false.

Clients communicate with Kotodama over MCP Streamable HTTP to create entities and relation types, propose or retract claims, record events, and query knowledge by entity, relation type, or point in time. For remembered natural-language facts, `dream` reduces confidence to 80% after each unconfirmed 30-day period and marks the Claim `stale` below 0.2. Other expiring Claim types retain the existing direct stale transition. Dream never rewrites a Claim as false or physically deletes it.

## What an AI gains from Kotodama

After the Kotodama application or its supported plugin or extension is installed and configured for an AI client, the AI can use its tools to:

- retain structured knowledge across tasks and sessions instead of relying only on the current conversation;
- retrieve facts together with their source, confidence, knowledge subject, and valid time;
- keep contradictory reports side by side and reason about them without silently overwriting either one;
- distinguish "not known" from an explicitly negative claim;
- find related entities and reconstruct the knowledge context around a person, organization, object, or event;
- detect claims that need reconfirmation through `stale` status and `dream` processing.

For example, an AI can remember that a person belonged to an organization during a particular period, preserve both an official announcement and a conflicting report, and later answer with the applicable time and evidence. Kotodama provides storage and retrieval tools; the AI or MCP client must call those tools, and Kotodama does not automatically import conversations or update knowledge from the Internet.

Kotodama supplies server instructions during MCP initialization and exposes the `use_kotodama` MCP prompt. Explicit requests such as "remember this" can be processed in one call with the `remember_knowledge` tool. The raw statement is used only for in-memory term extraction and is never stored or returned. Schedules and events persist atomic actor, action, place, time range, and event structure for retrieval through `query_events`. `configure claude` and `configure codex` also install client hooks that prompt knowledge retrieval before an answer and knowledge review after it.

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

For the current development checkout, prepare the canonical settings file as described in [Source distribution](#source-distribution) before starting this foreground HTTP host.

```powershell
dotnet run --project src/Kotodama
```

Kotodama reads all application settings from `C:\Kotodama\config\kotodama.json`; it does not use environment variables or discover another settings file. The server listens on `http://127.0.0.1:39280` by default. The stdio transport was removed in 0.18.0. Dream staging can use `Default`, `Memory`, or `File`. Connect MCP clients to `http://127.0.0.1:39280/mcp`. HTTP remains restricted to loopback. See [configuration](CONFIG.ja.md).

## MCP tools

`get_version`, `get_entity`, `get_knowledge_input`, `get_statement`, `search_entities`, `get_equivalent_entities`, `merge_similarity_groups`, `merge_namespace`, `create_entity`, `create_relation_type`, `update_relation_type`, `delete_relation_type`, `create_event`, `query_events`, `query_relations`, `query_claims`, `get_neighbors`, `get_knowledge_context`, `propose_claim`, `remember_knowledge`, `retract_claim`, `reactivate_claim`, `delete_claim`, `run_dream`, `create_tag`, `list_tags`, `rename_tag`, `add_tag_alias`, `merge_tags`, `set_knowledge_tags`, `query_tagged_inputs`, `query_tagged_statements`, and `query_tagged_claims` (33 tools). The two statement-named tools only return typed Protocol 2 migration errors.

Optional `remember_knowledge.input.tags` atomically tags the text-free input and its claims. Tags support namespace-scoped Unicode normalization, exact AND/OR search, previewed bulk attachment, renaming, aliases, and ID-preserving merges. See the [tag contract and migration guide](KNOWLEDGE_TAGS.ja.md). `Kotodama call <tool> <arguments.json>` invokes the same tools through the running HTTP service; `call-help` prints usage.

`remember_knowledge` requires `input.statement`, `input.entities`, and `input.relations`; every entity must be one proper name, noun, short noun phrase, or identifier. It stores a text-free `knowledge_inputs` row, unordered `input_terms`, concepts, and claims. Structural failure remains unpersisted after every retry. The removed `get_statement` and `query_tagged_statements` calls return a typed Protocol 2 incompatibility result. See the [structured knowledge contract](STRUCTURED_KNOWLEDGE.ja.md) and [design decisions](STRUCTURED_KNOWLEDGE_DESIGN.ja.md).

Administrative tools also support claim reactivation and explicit physical deletion, plus RelationType update and deletion. RelationTypes that are still referenced are not deleted. Dream runs periodically (3600 seconds by default), daily logs are written under the deployment `logs` directory, and `kotodama backup <destination.db>` creates an online SQLite backup.

The storage model preserves conflicting positive and negative claims, distinguishes Source from knowledge subject, normalizes symmetric edges, supports temporal querying, and gradually reduces unconfirmed remembered knowledge before excluding it as `stale` from default queries.

## Installation

### Current development MSI (user tray)

The current source builds a user-scoped tray application. These instructions do not provide a public download link for that development package. Build `Kotodama-0.21.0-user-x64.msi` using [the development guide (Japanese)](DEVELOPMENT.ja.md#msi生成) or [the installer build guide](installer/README.md).

The package installs under `C:\Kotodama`: `bin` holds executables, `config` the canonical settings file, `data` the SQLite database, and `logs` daily logs. Complete the initial folder permission setup and legacy service removal in [the installation guide (Japanese)](INSTALLATION.ja.md) first; that initial preparation requires administrator privileges.

After preparation, install or update from a non-elevated PowerShell:

```powershell
msiexec.exe /i "Kotodama-0.21.0-user-x64.msi" /norestart
& C:\Kotodama\bin\Kotodama.Tray.exe
```

The per-user MSI registers `Kotodama.Tray.exe` in HKCU Run and the user PATH. The tray runs the HTTP server after logon and provides status, configuration, logs, restart, and exit. Exit the tray before updating and start it afterward. It does not run before sign-in or after sign-out. The MSI provisions `C:\Kotodama\config\kotodama.json` only when absent; upgrades and uninstall preserve that file and non-empty data directories.

### Published legacy MSI (0.20.0, Windows service)

The following download is the older Windows service package; it does not contain the current development tray application:

[Kotodama-0.20.0-x64.msi](https://github.com/katsushoe/Kotodama/releases/download/v0.20.0/Kotodama-0.20.0-x64.msi)

```powershell
Get-FileHash .\Kotodama-0.20.0-x64.msi -Algorithm SHA256
Start-Process msiexec.exe -Verb RunAs -Wait `
  -ArgumentList '/i "Kotodama-0.20.0-x64.msi" /norestart'
```

This legacy MSI installs under `C:\Kotodama` and uses administrator privileges for its machine installation and Windows service. Use the migration guide above when moving to the development tray package; do not install both packages together.

### Published legacy ZIP (0.20.0)

Download [Kotodama-0.20.0-win-x64.zip](https://github.com/katsushoe/Kotodama/releases/download/v0.20.0/Kotodama-0.20.0-win-x64.zip), verify its SHA-256, and extract it to a writable directory:

```powershell
Get-FileHash .\Kotodama-0.20.0-win-x64.zip -Algorithm SHA256
Expand-Archive .\Kotodama-0.20.0-win-x64.zip -DestinationPath C:\Tools\Kotodama
& C:\Tools\Kotodama\Kotodama.exe
```

This is the legacy 0.20.0 ZIP. To build a ZIP containing the current development tray application, use [the development guide (Japanese)](DEVELOPMENT.ja.md#portable-zip生成).

The ZIP is self-contained and does not require a separately installed .NET Runtime. It does not register Kotodama with Windows, modify `PATH`, or provide automatic upgrades. Preserve the extracted `data` directory when replacing a version.

### Claude Desktop Extension (discontinued)

The Claude Desktop Extension (DXT) was discontinued in 0.18.0 together with the stdio transport. If it is installed, disable or remove the Kotodama extension in Claude Desktop under `Settings > Extensions`; an older extension that opens the same database as the MSI service can modify its migrated schema. Claude Code connects to the resident HTTP service.

### Source distribution

On Windows x64, install the .NET 10 SDK with Windows Desktop references and prepare the writable folders using [the installation guide (Japanese)](INSTALLATION.ja.md#保存先と初回準備). Then build the current development branch:

```powershell
git clone --branch develop https://github.com/katsushoe/Kotodama.git
Set-Location Kotodama
dotnet restore Kotodama.slnx
dotnet build Kotodama.slnx -c Release --no-restore
New-Item -ItemType Directory -Force C:\Kotodama\config | Out-Null
if (-not (Test-Path C:\Kotodama\config\kotodama.json)) { Copy-Item installer\kotodama.json C:\Kotodama\config\kotodama.json }
dotnet run --project src\Kotodama.Tray -c Release --no-build
```

This starts the development tray application and its HTTP host. To run only the foreground HTTP host, use `dotnet run --project src/Kotodama -c Release --no-build`. Only one tray or foreground host may use the configured endpoint at a time. Both read the canonical settings file; a source launch does not register login startup or AI clients.
