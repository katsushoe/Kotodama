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

テストには規則検証、Open World、競合Claim、対称Relation、時間境界、Source、Event、dreamの各格納方式、実並行処理、障害注入、MCP stdio／Streamable HTTP結合を含みます。

## MSI生成

```powershell
dotnet publish src/Kotodama/Kotodama.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts/publish/win-x64

$publishDir = (Resolve-Path artifacts/publish/win-x64).Path
.tools/wix build installer/Package.wxs `
  -arch x64 `
  -d ProductVersion=0.11.2 `
  -d PublishDir=$publishDir `
  -o artifacts/release/Kotodama-0.11.2-x64.msi
```

## Claude Desktop Extension生成

```powershell
& desktop-extension/Build-DesktopExtension.ps1 `
  -OutputDirectory artifacts/release `
  -Configuration Release `
  -Runtime win-x64
```

`artifacts/release/Kotodama-<version>-win-x64.dxt`が生成されます。DXTはZIP互換形式であり、ルートの`manifest.json`と`server/Kotodama.exe`を含みます。生成後はClaude DesktopのExtension Developer画面から実機インストールし、Tool discovery、`use_kotodama` Prompt、DB永続化、Extension更新後のデータ保持を確認します。

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
- Windows x64 DXT生成
- Install、Upgrade、Uninstall実機確認
- Version表示確認
- MSI SHA-256作成・照合
- インストール済み実行ファイルとのMCP stdio通信確認
- 既定DBが`C:\Kotodama\data`へ作成されること
- 利用者データがUpgrade／Uninstallで不用意に削除されないこと
- Claude DesktopでDXTのInstall、Tool／Prompt discovery、更新、削除を実機確認
- Codex Plugin／Skill validator合格、Agent登録・削除、MCP Tool呼び出しを確認
