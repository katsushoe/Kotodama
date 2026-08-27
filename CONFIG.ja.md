# 設定

Kotodamaの設定は環境変数で指定します。設定ファイルの自動読み込みはありません。

## 環境変数

| 名前 | 値 | 省略時 | 内容 |
|---|---|---|---|
| `KOTODAMA_DB` | SQLite DBの絶対または相対パス | 配置により決定 | Knowledge Graphの永続化先 |
| `KOTODAMA_DREAM_TEMP_STORE` | `Default`、`Memory`、`File` | `Default` | dream一時テーブルのSQLite格納方式 |
| `KOTODAMA_TRANSPORT` | `stdio`、`http` | `stdio` | MCP Transport |
| `KOTODAMA_HTTP_URL` | loopbackの絶対HTTP／HTTPS URL | なし | HTTPモードの待受URL。HTTPモードでは必須 |

`KOTODAMA_DREAM_TEMP_STORE`は大文字小文字を区別しません。不明な値はエラーにせず`Default`として扱います。

`KOTODAMA_TRANSPORT`は大文字小文字を区別しません。不明な値、HTTPモードでのURL未指定、loopback以外のhost、URL内のpath・query・fragment・userinfoは起動エラーです。MCP endpointは指定URLの`/mcp`です。認証とアクセス制御がないため、loopback以外には公開できません。

## DBパスの決定順序

1. `KOTODAMA_DB`が設定されていれば、その値を使用します。
2. 実行ファイルの1階層上に`data`ディレクトリが存在すれば、`data\kotodama.db`を使用します。
3. それ以外は実行ファイルと同じディレクトリの`kotodama.db`を使用します。

MSI版では2番目が適用され、`C:\Kotodama\data\kotodama.db`になります。

## 設定例

現在のPowerShellプロセスだけへ設定します。

```powershell
$env:KOTODAMA_DB = "D:\KotodamaData\kotodama.db"
$env:KOTODAMA_DREAM_TEMP_STORE = "Memory"
& "C:\Kotodama\bin\Kotodama.exe"
```

Streamable HTTPで起動する例です。

```powershell
$env:KOTODAMA_TRANSPORT = "http"
$env:KOTODAMA_HTTP_URL = "http://127.0.0.1:39280"
& "C:\Kotodama\bin\Kotodama.exe"
```

MCPクライアントの接続先は`http://127.0.0.1:39280/mcp`です。

データベースの親ディレクトリは事前に作成し、Kotodama実行ユーザーへ書き込み権限を付与してください。認証Token等の秘密情報を扱う設定はありません。
