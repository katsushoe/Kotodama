# 開発と検証

## 環境

- C# / .NET 10
- SQLite / `Microsoft.Data.Sqlite`
- xUnit / FluentAssertions
- WiX Toolset 5

## ビルドとテスト

```powershell
dotnet restore Kotodama.slnx
dotnet build Kotodama.slnx -c Release --no-restore
dotnet test Kotodama.slnx -c Release --no-restore
dotnet format Kotodama.slnx --verify-no-changes --no-restore
```

テストには規則検証、Open World、競合Claim、対称Relation、時間境界、Source、Event、dreamの各格納方式、実並行処理、障害注入、MCP Streamable HTTP結合を含みます。

## MSI生成

```powershell
dotnet publish src/Kotodama/Kotodama.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts/publish/win-x64

$publishDir = (Resolve-Path artifacts/publish/win-x64).Path
.tools/wix build installer/Package.wxs `
  -arch x64 `
  -d ProductVersion=0.18.1 `
  -d PublishDir=$publishDir `
  -o artifacts/release/Kotodama-0.18.1-x64.msi
```

## Portable ZIP生成

`artifacts/publish/win-x64`の内容をZIPのルートへ格納し、`Kotodama-<version>-win-x64.zip`とします。ZIP内のパス区切りは`/`にしてください。Windows PowerShell 5.1の`System.IO.Compression.ZipFile`は`\`区切りのエントリを作成するため使用しません。

```powershell
python -c "import shutil; shutil.make_archive('artifacts/release/Kotodama-0.18.1-win-x64', 'zip', 'artifacts/publish/win-x64')"
```

Claude Desktop Extension（DXT）は0.18.0で廃止しました。KotodamaのMCPサーバーはStreamable HTTP専用です。

## Codexプラグイン検証

```powershell
$python = "<Codex bundled Python path>"
& $python "$env:USERPROFILE\.codex\skills\.system\plugin-creator\scripts\validate_plugin.py" plugins\kotodama
& $python "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" plugins\kotodama\skills\kotodama-knowledge
```

プラグインは`plugins/kotodama`、Agent templateは`plugins/kotodama/assets/kotodama-curator.toml`を正本とします。Agent templateはpublish時に`codex/kotodama-curator.toml`として同梱されます。

## リリース完了条件

- warning 0、error 0
- 全テスト合格
- format検証合格
- x64 MSI生成
- Portable ZIP生成（パス区切り`/`）
- Install、Upgrade、Uninstall実機確認
- Version表示確認
- MSI SHA-256作成・照合
- インストール済みHTTPサーバーとのMCP通信確認（`tools/Kotodama.HealthCheck`）
- 既定DBが`C:\Kotodama\data`へ作成されること
- 利用者データがUpgrade／Uninstallで不用意に削除されないこと
- Codex Plugin／Skill validator合格、Agent登録・削除、MCP Tool呼び出しを確認
