# Windowsインストール

Windows x64向けの自己完結型MSIを使用します。.NET Runtimeの別途導入は不要です。
[ADR-0002](docs/decisions/ADR-0002-user-tray.ja.md) に従い、通常ユーザー権限のトレイ常駐へ移行しました。

## 保存先と初回準備

- `C:\Kotodama\bin`: プログラム
- `C:\Kotodama\config\kotodama.json`: 唯一の設定ファイル
- `C:\Kotodama\data`: SQLite DB
- `C:\Kotodama\logs`: 日別ログ

初回だけ管理者が `installer/Initialize-UserInstall.ps1` を実行し、利用者のSIDへ
これらの保存先の変更権限を付与します。管理者とSYSTEMの権限は保持します。
旧Windowsサービス版がある場合はDBをバックアップし、`-RemoveMachineInstall` を指定して旧MSIを削除します。
SIDは通常ユーザーのPowerShellで `[Security.Principal.WindowsIdentity]::GetCurrent().User.Value` から取得します。

```powershell
# 管理者のPowerShellで実行（SIDとバックアップ先は利用環境の値を指定）
.\installer\Initialize-UserInstall.ps1 -UserSid '<利用者SID>' -BackupDirectory '<バックアップ先>' -RemoveMachineInstall
```

旧サービス版と新しいユーザー単位MSIを同時に導入しないでください。
固定保存先を使用するため、利用者はPC内の1ユーザーを想定します。

## Install / Upgrade

次を通常ユーザーのPowerShellで実行します。管理者として起動しません。

```powershell
msiexec.exe /i "Kotodama-0.21.0-user-x64.msi" /norestart
```

MSIはユーザーのPATH、HKCU Runのログイン時起動、Codexおよび導入済みClaude Codeの接続設定とHooksを登録します。
初回のログインを待たず起動する場合は `C:\Kotodama\bin\Kotodama.Tray.exe` を通常権限で実行します。
Codex用プラグインの配布元は `plugins\kotodama` です。
設定の読込元は `C:\Kotodama\config\kotodama.json` だけで、環境変数やユーザープロファイル側の設定は読みません。

トレイから状態確認、設定とログの表示、再起動、終了を操作します。再起動時に設定を読み直します。
ログイン前・ログアウト後は稼働しません。HTTP接続先は設定の `httpUrl` に `/mcp` を付けたURLです。
既定値は `http://127.0.0.1:39280/mcp` です。
CLIの業務操作は `Kotodama.exe call` から稼働中のHTTPサーバーへ接続します。

更新前にDBをバックアップし、新しいMSIを同じ通常権限のコマンドで導入します。
トレイを終了してから更新し、終了後に再度起動してください。
既存設定と非空のDBディレクトリは保持されます。

## Uninstall

Windowsのインストール済みアプリ、または通常権限の `msiexec.exe /x {PRODUCT-CODE}` で削除します。
トレイ起動登録とクライアント設定を解除し、プログラムを削除します。設定ファイルと非空のDBディレクトリは保持します。

## ソースからのビルド

.NET 10 SDKとWindows Desktopの参照が必要です。[installer/README.md](installer/README.md) の手順を使用します。
コンソールでHTTPホストを起動する場合は `Kotodama.exe --http` を使用します。
