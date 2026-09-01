# 運用手順

## バックアップ

```powershell
kotodama backup "D:\KotodamaBackup\kotodama.db"
```

稼働中のDBからSQLite Online Backup APIで整合した単一DBファイルを作成します。出力先の親ディレクトリが存在しない場合は自動作成します。バックアップ中にKotodamaを停止する必要はありません。

## 稼働確認

MCP接続を初期化し、`get_version`が`Kotodama`と`0.11.4`を返すことを確認します。プロセスの存在確認だけでは正常性を保証しません。

Claude Codeでは`~/.claude/settings.json`の`UserPromptSubmit`と`Stop`に`--integration-id kotodama`を含むHookがあることを確認します。`kotodama configure claude`はMCP接続とHooksを登録し、`kotodama unconfigure claude`はKotodama固有設定だけを削除します。

「覚えておいて」等の明示的な永続化依頼では、内蔵メモリや読み込み済み文書だけで完了せず、Kotodama内を検索して未登録ならClaimを登録します。同等のactive Claimがある場合は重複登録しません。

Codexでは`~/.codex/hooks.json`の`UserPromptSubmit`と`Stop`に`--integration-id kotodama`を含むHookがあることを確認します。初回の信頼確認で内容を確認して許可してください。`kotodama unconfigure codex`はKotodama固有HookとMCP接続だけを削除します。

MSI版のStreamable HTTP接続先は`http://127.0.0.1:39280/mcp`です。ログオン時に`Kotodama MCP Server` Scheduled Taskがウィンドウを表示せず起動します。`KOTODAMA_HTTP_TOKEN`設定時はBearer認証が必須です。認証の有無にかかわらずloopback以外へ転送・公開しないでください。

## dream運用

鮮度管理を使用する場合は、RelationTypeへ`freshnessPolicy`と`refreshAfterSeconds`を設定します。HTTP常駐プロセスがdreamを定期実行し、間隔は`KOTODAMA_DREAM_INTERVAL_SECONDS`（既定3600秒）で設定します。

`examined`は評価対象数、`markedStale`は実更新数です。並行更新や別dreamとの競合により、`examined`より`markedStale`が少ないことは正常です。

## 復旧

1. Kotodamaを停止します。
2. 現在のDB関連ファイルを別の退避先へコピーします。
3. `kotodama backup`で作成したDBファイルを設定されたDBパスへ復元します。
4. Kotodamaを起動し、`get_version`、`get_entity`、`query_claims`で確認します。
5. 必要な場合だけ`run_dream`を実行します。

## UpgradeとUninstall

MSI UpgradeおよびUninstallは、利用者が作成した非空の`data`、`config`、`logs`を意図的に削除しません。DBの削除はMSIへ任せず、バックアップ後に利用者が明示的に行ってください。

## 障害調査

| 症状 | 確認事項 |
|---|---|
| `unable to open database file` | DB親ディレクトリの存在と書き込み権限、`KOTODAMA_DB` |
| 起動直後に終了コード1 | 標準エラー出力、DBパス、SQLiteファイル、ディスク空き容量 |
| Toolが見つからない | MCP初期化結果と17個のTool一覧、接続先実行ファイル |
| Claimが検索されない | `includeRetracted`、`validAt`、Entity ID、RelationType名 |
| dreamで更新されない | Claim状態、freshness policy、refresh秒、基準日時、並行更新 |
| `markedStale`が0 | 期限未到来、既にstale、退避後の更新、他dreamによる先行更新 |

標準出力はMCPプロトコル専用です。診断メッセージを標準出力へ混在させないでください。
