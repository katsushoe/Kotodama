# Kotodama

SQLite based temporal, epistemic and weighted Knowledge Graph MCP server for AI agents.

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

## Windows installation

The x64 MSI installs Kotodama under `C:\Kotodama`:

- `bin`: executable and runtime files
- `config`: local configuration
- `data`: SQLite databases and application data
- `logs`: logs

Configuration, databases, and logs are not included in the MSI. Non-empty data directories remain when upgrading or uninstalling. Set `KOTODAMA_DB` when the database should be stored outside the executable directory.
