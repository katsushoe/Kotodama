# Kotodama

Kotodamaは、AIエージェント向けの時間・認識主体・重みを扱えるSQLite Knowledge Graph MCPサーバーです。

## 主な性質

- Entity、RelationType、Relation、Claim、Source、Eventを分離して保存します。
- 同じRelationに対する肯定Claimと否定Claimを競合情報として共存させます。
- 情報が存在しない場合はfalseと断定せず、空の検索結果をunknownとして扱います。
- Claimの有効期間、観測日時、最終確認日時、鮮度状態を保持します。
- dreamは期限切れのClaimを否定せず、`active`から`stale`へ変更します。
- stdioによるMCPサーバーとして13個のToolを提供します。

## インストール版の起動

実行ファイルは次の場所へインストールされます。

```text
C:\Kotodama\bin\Kotodama.exe
```

KotodamaはMCP stdioサーバーです。通常はMCPクライアントから子プロセスとして起動し、標準入力へJSON-RPCを送り、標準出力から応答を受け取ります。直接起動すると入力待ちになります。

既定のデータベースは次の場所です。

```text
C:\Kotodama\data\kotodama.db
```

## ソースからの起動

```powershell
$env:KOTODAMA_DB = "C:\data\kotodama.db"
dotnet run --project src/Kotodama
```

## 最初に読む文書

- [文書一覧](DOCUMENTS.ja.md)
- [設定](CONFIG.ja.md)
- [MCP Tool仕様](MCP_TOOLS.ja.md)
- [dream仕様](DREAM.ja.md)
- [運用手順](OPERATIONS.ja.md)
- [Windowsインストール](INSTALLATION.ja.md)

## v0.1の制約

- HTTP Transport、認証、暗号化、アクセス制御は提供しません。信頼できるローカルプロセス間で使用してください。
- dreamのWindows Service、常駐処理、Scheduled Task登録は未実装です。`run_dream`を外部スケジューラーまたはMCPクライアントから定期実行してください。
- RelationTypeの編集・削除、Claimの再有効化、データの物理削除、バックアップCLIは未実装です。
- ログは標準エラー出力へ送られます。MSIの`logs`ディレクトリへ自動保存する機能は未実装です。
