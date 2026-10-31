# 開発と検証

## 環境

- Windows x64 / C# / .NET 10 SDK（Windows Desktop参照を含む）
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

現在の開発版はユーザー単位のトレイ常駐MSIです。`Kotodama.Tray`をpublishすると、トレイ本体と参照先の`Kotodama.exe`を同梱できます。サーバー単体のpublishをMSI入力にしないでください。

```powershell
dotnet publish src/Kotodama.Tray/Kotodama.Tray.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts/publish/user-win-x64

$publishDir = (Resolve-Path artifacts/publish/user-win-x64).Path
.tools/wix build installer/Package.wxs `
  -arch x64 `
  -d ProductVersion=0.21.0 `
  -d PublishDir=$publishDir `
  -o artifacts/release/Kotodama-0.21.0-user-x64.msi
```

生成先に`Kotodama.Tray.exe`、`Kotodama.exe`、`codex/kotodama-curator.toml`が含まれることを確認します。MSIはHKCU Runへログイン時起動を登録します。初回の保存先権限の設定と旧サービス版からの移行は[Windowsインストール](INSTALLATION.ja.md)を参照してください。初回準備後のMSI導入・更新は通常ユーザー権限で行います。

## Portable ZIP生成

`artifacts/publish/user-win-x64`の内容をZIPのルートへ格納し、`Kotodama-<version>-user-win-x64.zip`とします。ZIP内のパス区切りは`/`にしてください。Windows PowerShell 5.1の`System.IO.Compression.ZipFile`は`\`区切りのエントリを作成するため使用しません。

```powershell
python -c "import shutil; shutil.make_archive('artifacts/release/Kotodama-0.21.0-user-win-x64', 'zip', 'artifacts/publish/user-win-x64')"
```

ZIPのトレイ起動は`Kotodama.Tray.exe`、単独のHTTP起動は`Kotodama.exe --http`です。同時に起動しないでください。どちらも`C:\Kotodama\config\kotodama.json`を必要とするため、展開前後に[初回準備](INSTALLATION.ja.md#保存先と初回準備)を行います。ZIP自身はPATH、ログイン時起動、AIクライアント設定を登録しません。

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
- 通常ユーザー権限でのInstall、Upgrade、Uninstall実機確認
- トレイ各メニューと再ログイン時の自動起動確認
- Version表示確認
- MSI SHA-256作成・照合
- インストール済みHTTPサーバーとのMCP通信確認（`tools/Kotodama.HealthCheck`）
- 唯一の設定`C:\Kotodama\config\kotodama.json`と既定DB保存先`C:\Kotodama\data`を確認
- プログラム・設定・DB・ログの保存先へ対象ユーザーが書き込めること
- 利用者データがUpgrade／Uninstallで不用意に削除されないこと
- Codex Plugin／Skill validator合格、Agent登録・削除、MCP Tool呼び出しを確認
