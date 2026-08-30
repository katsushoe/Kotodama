# Kotodama

Kotodamaは、AIエージェント向けの時間・認識主体・重みを扱えるSQLite Knowledge Graph MCPサーバーです。

## Kotodamaの概要

Kotodamaは、AIエージェントが利用する知識をローカルのSQLiteへ永続化し、MCP stdioまたはStreamable HTTP経由で登録・検索するサーバーです。Entity間の関係だけでなく、その関係を誰がどのSourceに基づいて主張したか、確信度、有効期間、観測日時、最終確認日時、鮮度も保持します。

同じ関係について肯定と否定、複数のSource、異なる確信度を上書きせず共存させます。検索結果が空であることは「未知」を意味し、「偽」とは断定しません。定期処理`dream`は現在性を保証できなくなったClaimを`stale`にしますが、偽への変更や確信度の書き換えは行いません。

MCPクライアントはEntityとRelationTypeの作成、Claimの提案・撤回、Eventの記録、Entity・RelationType・時点を条件とした知識検索を行えます。

## KotodamaでAIができること

KotodamaをインストールしてMCPサーバーとして登録すると、MCP対応AIはKotodamaのToolを使って次のことができます。

- 現在の会話だけに依存せず、複数のタスクやセッションをまたいで構造化された知識を保持できます。
- 事実だけでなく、Source、確信度、認識主体、有効な時点を併せて取得できます。
- 矛盾する報告を一方で上書きせず、両方を残して比較・判断できます。
- 「知らない」ことと、明示的に否定されたことを区別できます。
- 人、組織、物、Eventに関連するEntityをたどり、周辺のKnowledge Contextを取得できます。
- `stale`状態と`dream`処理により、再確認が必要なClaimを識別できます。

例えば、「ある人物が特定期間に組織へ所属していた」という知識を保持し、公式発表とそれに反する報告を両方保存したうえで、後から該当時点と根拠を伴って回答できます。Kotodamaが提供するのは知識の保存・検索Toolです。AIまたはMCPクライアントがToolを呼び出す必要があり、会話の自動取り込みやInternet上の知識の自動更新は行いません。

KotodamaはMCP初期化時にServer Instructionsを返し、`use_kotodama` MCP Promptも提供します。`configure claude`と`configure codex`は各クライアントのHooksも設定し、回答前の検索と応答後の知識登録確認を自動化します。生の会話履歴は保存せず、AIが再利用可能と判断した、根拠のある構造化知識だけをMCP Tool経由で登録します。

Codex向けには`plugins/kotodama`にMCP接続と`kotodama-knowledge` Skillを含むプラグインを提供します。`configure codex`は`kotodama-curator`カスタムAgentもユーザースコープへ登録し、応答後の知識整理を分離Contextで実行できるようにします。Agentが利用不能な場合は親Agentが同じ確認を行います。

## Kotodamaのデータモデル

```text
Entity --< 有向／対称 Relation >-- Entity
                       |
                       +-- Claim -- 任意 --> Source
                             |
                             +-- 任意の認識主体（Entity）

Entity -- Eventとしての追加情報 --> Event
```

- **Entity**: 人、組織、物、概念、Eventなどの識別対象です。
- **RelationType**: Relationの意味、方向、strengthの可否、鮮度規則を定義します。
- **Relation**: 2つのEntityを結ぶ有向または対称の構造です。対称RelationはEntity ID順に正規化します。
- **Claim**: Relationについての主張です。極性、確信度、帰属確信度、strength、認識主体、Source、時点、状態を保持します。
- **Source**: Claimの根拠となる文書、発言、URLなどです。Claimの認識主体とは別に管理します。
- **Event**: 発生日時、actor、action、objectまたは値を持つEntityです。

Claimは明示的な撤回で`active -> retracted`、`dream`で`active -> stale`へ遷移します。`stale`は再確認が必要という意味であり、偽ではありません。有効期間は`valid_from`を含み、`valid_to`を含みません。詳細は[Knowledge Model](KNOWLEDGE_MODEL.ja.md)を参照してください。

## 主な性質

- Entity、RelationType、Relation、Claim、Source、Eventを分離して保存します。
- 同じRelationに対する肯定Claimと否定Claimを競合情報として共存させます。
- 情報が存在しない場合はfalseと断定せず、空の検索結果をunknownとして扱います。
- Claimの有効期間、観測日時、最終確認日時、鮮度状態を保持します。
- dreamは期限切れのClaimを否定せず、`active`から`stale`へ変更します。
- stdioとStreamable HTTPによるMCPサーバーとして17個のToolを提供します。

## MSIインストーラーを使う場合

[Kotodama-0.11.1-x64.msi](https://github.com/katsushoe/Kotodama/releases/download/v0.11.1/Kotodama-0.11.1-x64.msi)をダウンロードし、SHA-256を照合してから管理者権限で実行します。

```powershell
Get-FileHash .\Kotodama-0.11.1-x64.msi -Algorithm SHA256
Start-Process msiexec.exe -Verb RunAs -Wait `
  -ArgumentList '/i "Kotodama-0.11.1-x64.msi" /norestart'
```

インストール先は`C:\Kotodama`です。Windowsのインストール済みアプリへ登録され、UpgradeとUninstallに対応します。

## Claude Desktop Extensionを使う場合

Windows版の`Kotodama-<version>-win-x64.dxt`を取得し、Claude Desktopの`Settings > Extensions > Advanced settings > Install Extension...`から選択します。インストール時にKotodamaのデータディレクトリを指定してください。Claude DesktopはDXT内のKotodamaをstdio MCPサーバーとして起動します。

DXTはMCP Tool、Server Instructions、`use_kotodama` Promptを提供します。Claude DesktopにはClaude Code用Hookとサブエージェントがないため、会話ごとの自動登録は保証されません。確実に知識登録を検討させる場合は、会話で`use_kotodama` Promptを選択してください。DXTを削除しても、指定したデータディレクトリは削除されません。

## ZIP配布を使う場合

[Kotodama-0.11.1-win-x64.zip](https://github.com/katsushoe/Kotodama/releases/download/v0.11.1/Kotodama-0.11.1-win-x64.zip)をダウンロードし、書き込み可能な任意の場所へ展開します。

```powershell
Get-FileHash .\Kotodama-0.11.1-win-x64.zip -Algorithm SHA256
Expand-Archive .\Kotodama-0.11.1-win-x64.zip -DestinationPath C:\Tools
& C:\Tools\Kotodama\bin\Kotodama.exe
```

ZIPは自己完結型で、別途.NET Runtimeを必要としません。Windowsへの製品登録、`PATH`変更、自動Upgradeは行いません。更新時は展開先の`data`ディレクトリを保持してください。

## ソース配布を使う場合

.NET 10 SDKをインストールし、Gitから取得してビルドします。

```powershell
git clone https://github.com/katsushoe/Kotodama.git
Set-Location Kotodama
git checkout v0.11.1
dotnet restore Kotodama.slnx
dotnet build Kotodama.slnx -c Release --no-restore
New-Item -ItemType Directory -Force data | Out-Null
$env:KOTODAMA_DB = (Join-Path $PWD 'data\kotodama.db')
dotnet run --project src\Kotodama -c Release --no-build
```

## 起動時の注意

MSI版の実行ファイルは次の場所です。

```text
C:\Kotodama\bin\Kotodama.exe
```

Kotodamaは既定ではMCP stdioサーバーです。通常はMCPクライアントから子プロセスとして起動し、標準入力へJSON-RPCを送り、標準出力から応答を受け取ります。直接起動すると入力待ちになります。

Streamable HTTPで起動する場合は次のように設定します。

```powershell
$env:KOTODAMA_TRANSPORT = "http"
$env:KOTODAMA_HTTP_URL = "http://127.0.0.1:39280"
$env:KOTODAMA_HTTP_TOKEN = "十分に長いランダムなtoken"
& "C:\Kotodama\bin\Kotodama.exe"
```

接続先は`http://127.0.0.1:39280/mcp`です。`KOTODAMA_HTTP_TOKEN`を設定した場合は、MCPクライアントから同じ値をBearer tokenとして送信します。HTTP待受は認証の有無にかかわらずloopbackに制限されます。

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

## 現在の制約

- HTTPでは任意のBearer token認証を利用できます。利用者別の権限分離とTLS終端は提供しないため、信頼できる端末のloopbackで使用してください。

## 定期dream・バックアップ・ログ

- HTTP常駐時はdreamを既定3600秒間隔で実行します。`KOTODAMA_DREAM_INTERVAL_SECONDS`で変更できます。
- `kotodama backup <出力先.db>`でSQLiteオンラインバックアップを作成できます。
- MSI版のログは`C:\Kotodama\logs\kotodama-YYYYMMDD.log`へ日別保存します。
- Claim物理削除とRelationType削除は取り消せません。RelationTypeは使用中の場合、削除を拒否します。
