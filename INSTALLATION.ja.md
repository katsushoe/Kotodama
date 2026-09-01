# Windowsインストール

## 要件

- Windows x64
- 管理者権限
- 配布物: `Kotodama-0.11.4-x64.msi`

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
Start-Process msiexec.exe -Verb RunAs -Wait -ArgumentList '/i "Kotodama-0.11.4-x64.msi" /norestart'
```

無人Installでは`/qn`を追加します。完了後、Windowsのインストール済みアプリに`Kotodama 0.11.4`が表示されること、`C:\Kotodama\bin\Kotodama.exe`のFile Versionが`0.11.4.0`であること、新しいターミナルで`kotodama`が解決できることを確認します。MSIは`C:\Kotodama\bin`をシステムPATHへ登録し、ログオンユーザーへ`Kotodama MCP Server` Scheduled Task、Codex、インストール済みの場合はClaude Codeの`kotodama` MCP設定とHooksを登録します。Codexには`%USERPROFILE%\.codex\agents\kotodama-curator.toml`も登録します。Codexは初回Hook実行時に信頼確認を表示する場合があります。KotodamaはScheduled Taskからコンソールウィンドウを表示せず起動します。

Codexプラグインの配布元はソースツリーの`plugins\kotodama`です。プラグインはKotodama HTTPサーバーの`http://127.0.0.1:39280/mcp`へ接続するため、MSIまたは`kotodama configure codex`による常駐設定を先に完了してください。導入後は新しいCodex Taskで`kotodama-knowledge` SkillとKotodama MCP Toolが利用可能であることを確認します。

Scheduled Taskは`http://127.0.0.1:39280/mcp`でKotodamaを起動します。CodexまたはClaude Codeを再起動するとStreamable HTTP Toolが利用可能になります。アンインストール時はScheduled Taskと両クライアントの設定を削除します。

## Upgrade

新しいMSIを同じコマンドでInstallします。UpgradeCodeは固定され、Major Upgradeとして旧版を置換します。既存の非空データディレクトリは保持します。Upgrade前にDBをバックアップしてください。

## Uninstall

Windowsのインストール済みアプリ、または登録されたProductCodeを使用してUninstallします。

```powershell
Start-Process msiexec.exe -Verb RunAs -Wait -ArgumentList '/x {PRODUCT-CODE} /norestart'
```

MSIはアプリ本体を削除します。利用者DB等が残っている非空ディレクトリは保持されます。完全削除はバックアップ後に利用者が明示的に行ってください。

## Hash確認

```powershell
Get-FileHash .\Kotodama-0.11.4-x64.msi -Algorithm SHA256
```

配布元が提示したSHA-256と一致する場合だけInstallしてください。

## Portable ZIP

ZIPは`Kotodama\bin`、`config`、`data`、`logs`の構成で展開されます。任意の書き込み可能な場所へ展開し、`bin\Kotodama.exe`をMCPクライアントから起動してください。自己完結型のため.NET Runtimeは不要です。

ZIPはWindowsへ製品登録せず、Upgrade／Uninstall機能もありません。更新時はKotodamaを停止し、`data`をバックアップしてから、`bin`だけを新しい配布物で置き換えてください。

## Claude Desktop Extension

`Kotodama-<version>-win-x64.dxt`はClaude Desktop専用のWindows x64配布物です。Claude Desktopの`Settings > Extensions > Advanced settings > Install Extension...`からDXTを選択し、SQLite DBとログを保存するデータディレクトリを指定します。

DXTは自己完結型のKotodamaをstdioで起動するため、MSIのインストール、Windows Service、Scheduled Taskは不要です。導入後は新しい会話でKotodama Toolが表示されることと、`use_kotodama` Promptを選択できることを確認します。Extensionの更新・削除前には指定したデータディレクトリをバックアップしてください。削除してもデータディレクトリは自動削除されません。

## ソース配布

ソースから使用する場合は.NET 10 SDKが必要です。Release Tagをcheckoutし、`dotnet restore`、`dotnet build -c Release`、`dotnet run`の順で実行します。DBパスは`KOTODAMA_DB`で明示することを推奨します。詳細なコマンドは[README.ja.md](README.ja.md)を参照してください。
