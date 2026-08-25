# Kotodama

SQLite based temporal, epistemic and weighted Knowledge Graph MCP server for AI agents.

Japanese documentation: [README.ja.md](README.ja.md)

Documentation index: [DOCUMENTS.ja.md](DOCUMENTS.ja.md)

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
