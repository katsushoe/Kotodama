# Kotodama

SQLite based temporal, epistemic and weighted Knowledge Graph MCP server for AI agents.

Japanese documentation: [README.ja.md](README.ja.md)

Documentation index: [DOCUMENTS.ja.md](DOCUMENTS.ja.md)

## Overview

Kotodama is a local MCP server that gives AI agents a persistent knowledge graph backed by SQLite. It stores not only relationships between entities, but also who asserted them, their sources, confidence, validity periods, observation and confirmation times, and freshness. Conflicting positive and negative claims coexist instead of overwriting each other, and an absent claim means unknown rather than false.

Clients communicate with Kotodama over MCP stdio to create entities and relation types, propose or retract claims, record events, and query knowledge by entity, relation type, or point in time. The `dream` process periodically marks claims whose currentness can no longer be assumed as `stale`; it does not rewrite them as false or change their confidence.

## What an AI gains from Kotodama

After Kotodama is installed and registered as an MCP server, an MCP-capable AI can use its tools to:

- retain structured knowledge across tasks and sessions instead of relying only on the current conversation;
- retrieve facts together with their source, confidence, knowledge subject, and valid time;
- keep contradictory reports side by side and reason about them without silently overwriting either one;
- distinguish "not known" from an explicitly negative claim;
- find related entities and reconstruct the knowledge context around a person, organization, object, or event;
- detect claims that need reconfirmation through `stale` status and `dream` processing.

For example, an AI can remember that a person belonged to an organization during a particular period, preserve both an official announcement and a conflicting report, and later answer with the applicable time and evidence. Kotodama provides storage and retrieval tools; the AI or MCP client must call those tools, and Kotodama does not automatically import conversations or update knowledge from the Internet.

Kotodama supplies server instructions during MCP initialization and exposes the `use_kotodama` MCP prompt. These give compatible clients a ready-made workflow for searching retained knowledge and registering reusable facts safely. Whether instructions or prompts are applied automatically depends on the MCP client; connecting Kotodama alone does not guarantee automatic conversation storage.

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

## MCP tools

`get_version`, `get_entity`, `search_entities`, `create_entity`, `create_relation_type`, `create_event`, `query_relations`, `query_claims`, `get_neighbors`, `get_knowledge_context`, `propose_claim`, `retract_claim`, and `run_dream`.

The v0.1 storage model preserves conflicting positive and negative claims, distinguishes Source from knowledge subject, normalizes symmetric edges, supports temporal querying, and marks expired currentness as `stale` without changing confidence.

## Installation

### MSI installer

Download [Kotodama-0.1.0-x64.msi](https://github.com/katsushoe/Kotodama/releases/download/v0.1.0/Kotodama-0.1.0-x64.msi), verify its SHA-256, and run it with administrator privileges:

```powershell
Get-FileHash .\Kotodama-0.1.0-x64.msi -Algorithm SHA256
Start-Process msiexec.exe -Verb RunAs -Wait `
  -ArgumentList '/i "Kotodama-0.1.0-x64.msi" /norestart'
```

The x64 MSI installs Kotodama under `C:\Kotodama`:

- `bin`: executable and runtime files
- `config`: local configuration
- `data`: SQLite databases and application data
- `logs`: logs

Configuration, databases, and logs are not included in the MSI. Non-empty data directories remain when upgrading or uninstalling. Set `KOTODAMA_DB` when the database should be stored outside the executable directory.

### Portable ZIP

Download [Kotodama-0.1.0-win-x64.zip](https://github.com/katsushoe/Kotodama/releases/download/v0.1.0/Kotodama-0.1.0-win-x64.zip), verify its SHA-256, and extract it to a writable directory:

```powershell
Get-FileHash .\Kotodama-0.1.0-win-x64.zip -Algorithm SHA256
Expand-Archive .\Kotodama-0.1.0-win-x64.zip -DestinationPath C:\Tools
& C:\Tools\Kotodama\bin\Kotodama.exe
```

The ZIP is self-contained and does not require a separately installed .NET Runtime. It does not register Kotodama with Windows, modify `PATH`, or provide automatic upgrades. Preserve the extracted `data` directory when replacing a version.

### Source distribution

Install the .NET 10 SDK, then clone and build the source:

```powershell
git clone https://github.com/katsushoe/Kotodama.git
Set-Location Kotodama
git checkout v0.1.0
dotnet restore Kotodama.slnx
dotnet build Kotodama.slnx -c Release --no-restore
$env:KOTODAMA_DB = (Join-Path $PWD 'data\kotodama.db')
New-Item -ItemType Directory -Force data | Out-Null
dotnet run --project src\Kotodama -c Release --no-build
```

Kotodama is an MCP stdio server, so a direct launch waits for JSON-RPC input. In normal use, configure an MCP client to launch the executable or `dotnet run` command.
