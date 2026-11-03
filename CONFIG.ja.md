# 設定

Kotodama本体の設定ファイルは `C:\Kotodama\config\kotodama.json` だけです。起動時に必須で読み込みます。ファイルがない、JSONが不正、値が無効な場合は起動に失敗します。環境変数、カレントディレクトリ、実行ファイル隣接の設定ファイルによる上書きや探索は行いません。

MSIは設定ファイルの初期版をインストールします。アップグレード時は利用者が編集したファイルを上書きせず、アンインストール後も保持します。

## 項目

| JSON名 | 型 | 既定値 | 説明 |
|---|---|---|---|
| `databasePath` | 文字列 | `C:\Kotodama\data\kotodama.db` | Knowledge GraphとMCP利用ログを保存するSQLite DB |
| `logDirectory` | 文字列 | `C:\Kotodama\logs` | 日別ログの保存先 |
| `dreamTempStore` | 文字列 | `Default` | dream一時領域の格納方式。`Default`、`Memory`、`File` |
| `dreamIntervalSeconds` | 正の整数 | `3600` | 常駐HTTPサービスのdream実行間隔（秒） |
| `transport` | 文字列 | `http` | `http`固定。stdioは0.18.0で廃止済み |
| `httpUrl` | 文字列 | `http://127.0.0.1:39280` | loopback上のHTTP／HTTPS待受URL。path等は指定不可 |
| `httpToken` | 文字列またはnull | `null` | 設定した場合はMCP要求にBearer認証を必須化 |

相対パスは設定ファイルのあるディレクトリを基準に解決します。待受URLのMCP endpointは `httpUrl` に `/mcp` を加えたものです。認証の有無にかかわらずloopback以外には公開できません。tokenはログへ出力しません。tokenを設定する場合、設定ファイルを信頼できる管理者だけが読めるようにしてください。

## CLI

`Kotodama call <tool> <arguments.json>` は、同じ設定ファイルにある `httpUrl` と `httpToken` を使って常駐サービスへ接続します。`Kotodama backup <出力先.db>` も `databasePath` のDBをバックアップします。CLI専用の設定上書きはありません。詳細は[知識タグ・CLI仕様](KNOWLEDGE_TAGS.ja.md)を参照してください。

Codexの `~/.codex/config.toml` やClaude Codeの `~/.claude/settings.json` は各クライアント自身が所有する接続登録ファイルです。Kotodama本体の設定ファイルとは別用途で、Codex／Claude連携を構成するときだけ更新します。
