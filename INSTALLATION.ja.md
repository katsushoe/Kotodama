# Windowsインストール

## 要件

- Windows x64
- 管理者権限
- 配布物: `Kotodama-0.20.0-x64.msi`

MSIは自己完結型の.NET実行環境を含むため、利用端末へ別途.NET Runtimeを入れる必要はありません。

## 配置

```text
C:\Kotodama\
├─ bin\       実行ファイルとRuntime
├─ config\    利用者設定用（自動読込なし）
├─ data\      SQLite DBとアプリデータ
└─ logs\      ログ保存用（自動保存なし）
```

## Install

```powershell
Start-Process msiexec.exe -Verb RunAs -Wait -ArgumentList '/i "Kotodama-0.20.0-x64.msi" /norestart'
```

無人Installでは`/qn`を追加します。完了後、Windowsのインストール済みアプリに`Kotodama 0.20.0`が表示されること、`C:\Kotodama\bin\Kotodama.exe`のFile Versionが`0.20.0.0`であること、新しいターミナルで`kotodama`が解決できることを確認します。MSIは`C:\Kotodama\bin`をシステムPATHへ登録し、Windowsサービス`Kotodama`を自動起動・`NT AUTHORITY\LocalService`実行で登録して開始します。インストールしたユーザーには、Codex、インストール済みの場合はClaude Codeの`kotodama` MCP設定とHooksを登録します。Codexには`%USERPROFILE%\.codex\agents\kotodama-curator.toml`も登録します。Codexは初回Hook実行時に信頼確認を表示する場合があります。

Codexプラグインの配布元はソースツリーの`plugins\kotodama`です。プラグインはKotodama HTTPサーバーの`http://127.0.0.1:39280/mcp`へ接続するため、MSIまたは`kotodama configure codex`による常駐設定を先に完了してください。導入後は新しいCodex Taskで`kotodama-knowledge` SkillとKotodama MCP Toolが利用可能であることを確認します。

サービスは`http://127.0.0.1:39280/mcp`で待ち受け、ログオン前から起動し、サインアウト後も継続します。異常終了時は自動で再起動します。CodexまたはClaude Codeを再起動するとStreamable HTTP Toolが利用可能になります。アンインストール時はサービスを停止・削除し、両クライアントの設定を削除します。

サービスの実行アカウント`LocalService`は、`C:\Kotodama\data`と`C:\Kotodama\logs`への書き込み権限を必要とします。既定の配置では`C:\`から継承される`Authenticated Users`の変更権限で書き込めます。ACLを制限している環境では、両ディレクトリへ`NT AUTHORITY\LOCAL SERVICE`の変更権限を付与してください。

## Upgrade

新しいMSIを同じコマンドでInstallします。UpgradeCodeは固定され、Major Upgradeとして旧版を置換します。既存の非空データディレクトリは保持します。Upgrade前にDBをバックアップしてください。

更新時はサービスを停止し、MSIのインストール先に一致するKotodamaプロセスも停止します。0.18.1以前から更新した場合は、旧版がログオン時起動に使っていた`Kotodama MCP Server` Scheduled Taskを停止・削除します。別ディレクトリのportable版を同名だけで停止しません。portable版の更新は個別に行います。

## Uninstall

Windowsのインストール済みアプリ、または登録されたProductCodeを使用してUninstallします。

```powershell
Start-Process msiexec.exe -Verb RunAs -Wait -ArgumentList '/x {PRODUCT-CODE} /norestart'
```

MSIはアプリ本体を削除します。利用者DB等が残っている非空ディレクトリは保持されます。完全削除はバックアップ後に利用者が明示的に行ってください。

## Hash確認

```powershell
Get-FileHash .\Kotodama-0.20.0-x64.msi -Algorithm SHA256
```

配布元が提示したSHA-256と一致する場合だけInstallしてください。

## Portable ZIP

任意の書き込み可能な場所へ展開し、展開先の`Kotodama.exe`を起動してください。KotodamaはStreamable HTTPサーバーとして`http://127.0.0.1:39280`で待ち受けるため、MCPクライアントは`http://127.0.0.1:39280/mcp`へ接続します。MSI版と同じポートを使うため、同じ端末でMSI版と同時に起動しないでください。自己完結型のため.NET Runtimeは不要です。

ZIPはWindowsへ製品登録せず、Upgrade／Uninstall機能もありません。更新時はKotodamaを停止し、DBをバックアップしてから、実行ファイル一式を新しい配布物で置き換えてください。

## Claude Desktop Extension（廃止）

Kotodama 0.18.0でstdio MCPサーバーを廃止したため、Claude Desktop Extension（DXT）の配布を終了しました。DXTは同梱の実行ファイルを別プロセスとして起動するため、MSI版と同じDBを旧版で開くと移行済みスキーマを変更するおそれがあります。導入済みの場合は、Claude Desktopの`Settings > Extensions`でKotodama拡張を無効化または削除してください。削除してもデータディレクトリは自動削除されません。Claude Codeからは常駐HTTPサーバーへ接続します。

## ソース配布

ソースから使用する場合は.NET 10 SDKが必要です。Release Tagをcheckoutし、`dotnet restore`、`dotnet build -c Release`、`dotnet run`の順で実行します。DBパスは`KOTODAMA_DB`で明示することを推奨します。詳細なコマンドは[README.ja.md](README.ja.md)を参照してください。
