# 設定

Kotodamaの設定は環境変数で指定します。設定ファイルの自動読み込みはありません。

## 環境変数

| 名前 | 値 | 省略時 | 内容 |
|---|---|---|---|
| `KOTODAMA_DB` | SQLite DBの絶対または相対パス | 配置により決定 | Knowledge Graphの永続化先 |
| `KOTODAMA_LOG_DIR` | ログディレクトリの絶対または相対パス | 配置により決定 | 日別ログの保存先 |
| `KOTODAMA_DREAM_TEMP_STORE` | `Default`、`Memory`、`File` | `Default` | dream一時テーブルのSQLite格納方式 |
| `KOTODAMA_DREAM_INTERVAL_SECONDS` | 1以上の整数 | `3600` | HTTP常駐時のdream実行間隔（秒） |
| `KOTODAMA_TRANSPORT` | `stdio`、`http` | `stdio` | MCP Transport |
| `KOTODAMA_HTTP_URL` | loopbackの絶対HTTP／HTTPS URL | なし | HTTPモードの待受URL。HTTPモードでは必須 |
| `KOTODAMA_HTTP_TOKEN` | Bearer token文字列 | なし | 設定時は`/mcp`への全要求でBearer認証を必須化 |

`KOTODAMA_DREAM_TEMP_STORE`は大文字小文字を区別しません。不明な値はエラーにせず`Default`として扱います。

`KOTODAMA_DREAM_INTERVAL_SECONDS`が未設定、不正、0以下の場合は3600秒です。stdioモードでは定期dreamを起動しません。

`KOTODAMA_TRANSPORT`は大文字小文字を区別しません。不明な値、HTTPモードでのURL未指定、loopback以外のhost、URL内のpath・query・fragment・userinfoは起動エラーです。MCP endpointは指定URLの`/mcp`です。`KOTODAMA_HTTP_TOKEN`設定時は`Authorization: Bearer <token>`が必要です。tokenはログへ出力しません。認証の有無にかかわらずloopback以外には公開できません。

## DBパスの決定順序

1. `KOTODAMA_DB`が設定されていれば、その値を使用します。
2. 実行ファイルの1階層上に`data`ディレクトリが存在すれば、`data\kotodama.db`を使用します。
3. それ以外は実行ファイルと同じディレクトリの`kotodama.db`を使用します。

MSI版では2番目が適用され、`C:\Kotodama\data\kotodama.db`になります。

## ログパスの決定順序

1. `KOTODAMA_LOG_DIR`が設定されていれば、その値を使用します。
2. 実行ファイルの1階層上に`logs`ディレクトリが存在すれば、そのディレクトリを使用します。
3. それ以外は実行ファイルと同じディレクトリの`logs`を使用します。

Claude Desktop Extensionは、利用者が指定したデータディレクトリ内の`kotodama.db`と`logs`をそれぞれ`KOTODAMA_DB`と`KOTODAMA_LOG_DIR`へ設定します。

## 設定例

現在のPowerShellプロセスだけへ設定します。

```powershell
$env:KOTODAMA_DB = "D:\KotodamaData\kotodama.db"
$env:KOTODAMA_DREAM_TEMP_STORE = "Memory"
$env:KOTODAMA_DREAM_INTERVAL_SECONDS = "3600"
& "C:\Kotodama\bin\Kotodama.exe"
```

Streamable HTTPで起動する例です。

```powershell
$env:KOTODAMA_TRANSPORT = "http"
$env:KOTODAMA_HTTP_URL = "http://127.0.0.1:39280"
$env:KOTODAMA_HTTP_TOKEN = "十分に長いランダムなtoken"
& "C:\Kotodama\bin\Kotodama.exe"
```

MCPクライアントの接続先は`http://127.0.0.1:39280/mcp`です。

データベースの親ディレクトリは事前に作成し、Kotodama実行ユーザーへ書き込み権限を付与してください。tokenは環境変数を参照できる同一ユーザーのプロセスから読み取られる可能性があるため、端末を共有する場合はOSのユーザーを分離してください。
