# 設定

Kotodama v0.1の設定は環境変数で指定します。設定ファイルの自動読み込みはありません。

## 環境変数

| 名前 | 値 | 省略時 | 内容 |
|---|---|---|---|
| `KOTODAMA_DB` | SQLite DBの絶対または相対パス | 配置により決定 | Knowledge Graphの永続化先 |
| `KOTODAMA_DREAM_TEMP_STORE` | `Default`、`Memory`、`File` | `Default` | dream一時テーブルのSQLite格納方式 |

`KOTODAMA_DREAM_TEMP_STORE`は大文字小文字を区別しません。不明な値はエラーにせず`Default`として扱います。

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

データベースの親ディレクトリは事前に作成し、Kotodama実行ユーザーへ書き込み権限を付与してください。接続文字列、Token、URL等の秘密情報を扱う設定はv0.1にありません。
