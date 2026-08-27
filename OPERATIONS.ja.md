# 運用手順

## 稼働確認

MCP接続を初期化し、`get_version`が`Kotodama`と`0.6.0`を返すことを確認します。プロセスの存在確認だけでは正常性を保証しません。

Claude Codeでは`~/.claude/settings.json`の`UserPromptSubmit`と`Stop`に`--integration-id kotodama`を含むHookがあることを確認します。`kotodama configure claude`はMCP接続とHooksを登録し、`kotodama unconfigure claude`はKotodama固有設定だけを削除します。

MSI版のStreamable HTTP接続先は`http://127.0.0.1:39280/mcp`です。ログオン時に`Kotodama MCP Server` Scheduled Taskがウィンドウを表示せず起動します。認証未実装のためloopback以外へ転送・公開しないでください。

## dream運用

鮮度管理を使用する場合は、RelationTypeへ`freshnessPolicy`と`refreshAfterSeconds`を設定し、外部スケジューラーから`run_dream`を定期実行します。最短の更新間隔より十分短く、DB負荷を許容できる周期を選択してください。

`examined`は評価対象数、`markedStale`は実更新数です。並行更新や別dreamとの競合により、`examined`より`markedStale`が少ないことは正常です。

## バックアップ

v0.1にバックアップCLIはありません。Kotodamaプロセスを停止したうえで、DB本体と存在する場合は`-wal`、`-shm`を同一時点の組としてバックアップしてください。稼働中バックアップが必要な場合はSQLite Online Backup API対応を将来機能として実装してください。

## 復旧

1. Kotodamaを停止します。
2. 現在のDB関連ファイルを別の退避先へコピーします。
3. バックアップ一式を設定されたDBパスへ復元します。
4. Kotodamaを起動し、`get_version`、`get_entity`、`query_claims`で確認します。
5. 必要な場合だけ`run_dream`を実行します。

## UpgradeとUninstall

MSI UpgradeおよびUninstallは、利用者が作成した非空の`data`、`config`、`logs`を意図的に削除しません。DBの削除はMSIへ任せず、バックアップ後に利用者が明示的に行ってください。

## 障害調査

| 症状 | 確認事項 |
|---|---|
| `unable to open database file` | DB親ディレクトリの存在と書き込み権限、`KOTODAMA_DB` |
| 起動直後に終了コード1 | 標準エラー出力、DBパス、SQLiteファイル、ディスク空き容量 |
| Toolが見つからない | MCP初期化結果と13個のTool一覧、接続先実行ファイル |
| Claimが検索されない | `includeRetracted`、`validAt`、Entity ID、RelationType名 |
| dreamで更新されない | Claim状態、freshness policy、refresh秒、基準日時、並行更新 |
| `markedStale`が0 | 期限未到来、既にstale、退避後の更新、他dreamによる先行更新 |

標準出力はMCPプロトコル専用です。診断メッセージを標準出力へ混在させないでください。
