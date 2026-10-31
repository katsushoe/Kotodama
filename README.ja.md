# Kotodama

Kotodamaは、時間・認識主体・重みを扱えるSQLite Knowledge Graph MCPサーバーを中核に、Codex／Claude Code向けプラグイン、Hooks、Agentを提供するAI知識基盤です。

[English](README.md)

## Kotodamaの概要

Kotodamaは、AIエージェントが利用する知識をローカルのSQLiteへ永続化するMCPサーバーと、AIがその知識を自然な会話から安全に登録・検索するためのクライアント連携機能をまとめて提供します。Codexにはプラグイン、Skill、curator Agent、Hooksを、Claude CodeにはMCP設定とHooksを提供します。中核のMCPサーバーはStreamable HTTPで常駐し、Entity間の関係だけでなく、その関係を誰がどのSourceに基づいて主張したか、確信度、有効期間、観測日時、最終確認日時、鮮度も保持します。

同じ関係について肯定と否定、複数のSource、異なる確信度を上書きせず共存させます。検索結果が空であることは「未知」を意味し、「偽」とは断定しません。定期処理`dream`は、自然文から保存した知識が30日間再確認されないごとにconfidenceを80%へ減衰し、0.2未満で`stale`にします。偽への変更や物理削除は行いません。

MCPクライアントはEntityとRelationTypeの作成、Claimの提案・撤回、Eventの記録、Entity・RelationType・時点を条件とした知識検索を行えます。

## KotodamaでAIができること

Kotodama本体または対応プラグイン／拡張をインストールしてAIクライアントへ設定すると、AIはKotodamaのToolを使って次のことができます。

- 現在の会話だけに依存せず、複数のタスクやセッションをまたいで構造化された知識を保持できます。
- 事実だけでなく、Source、確信度、認識主体、有効な時点を併せて取得できます。
- 矛盾する報告を一方で上書きせず、両方を残して比較・判断できます。
- 「知らない」ことと、明示的に否定されたことを区別できます。
- 人、組織、物、Eventに関連するEntityをたどり、周辺のKnowledge Contextを取得できます。
- `stale`状態と`dream`処理により、再確認が必要なClaimを識別できます。

例えば、「ある人物が特定期間に組織へ所属していた」という知識を保持し、公式発表とそれに反する報告を両方保存したうえで、後から該当時点と根拠を伴って回答できます。Kotodamaが提供するのは知識の保存・検索Toolです。AIまたはMCPクライアントがToolを呼び出す必要があり、会話の自動取り込みやInternet上の知識の自動更新は行いません。

KotodamaはMCP初期化時にServer Instructionsを返し、`use_kotodama` MCP Promptも提供します。「覚えて」「記憶して」「今後参照して」等の明示依頼には、自然文を一回の呼び出しで保存する`remember_knowledge` Toolを提供します。予定や出来事は原文に加えてActor、Place、期間、Eventを保存でき、`query_events`で構造検索できます。`configure claude`と`configure codex`は各クライアントのHooksも設定し、回答前の検索と知識登録確認を自動化します。生の会話履歴は保存せず、直接根拠がある事実は長期利用価値が不確かな場合も保存候補とし、未再確認の知識はdreamで段階的に薄れます。

Codex向けには`plugins/kotodama`にMCP接続と`kotodama-knowledge` Skillを含むプラグインを提供します。`configure codex`は`kotodama-curator`カスタムAgentもユーザースコープへ登録し、応答後の知識整理を分離Contextで実行できるようにします。Agentが利用不能な場合は親Agentが同じ確認を行います。

自動登録方式はクライアントごとに異なります。Claude CodeではUserPromptSubmit HookとServer Instructionsが、Codexでは応答完了Hookが、明示的な記憶依頼がない通常の会話も知識候補として確認します。Claude Codeでは毎ターンの追加応答を避けるため、応答完了（Stop）Hookを使用しません。ユーザーの事実記述または識別済みSourceで裏付けられた知識だけを登録し、会話本文や根拠のないAI生成文は保存しません。

DBへの新規記録が成功した場合、AIは利用者へ`Kotodamaに記録しました`と通知します。既存知識との重複、登録見送り、確認待ち、拒否、失敗では、この成功通知を表示しません。

## Kotodamaのデータモデル

`remember_knowledge`は`input.statement/entities/relations`を必須とします。`statement`はメモリ上で語彙抽出にだけ使い、DB・ログ・応答へ保存しません。DBには本文を持たない`knowledge_inputs`、順序やoffsetを持たない`input_terms`、原子的なEntity・Relation・Claimだけを保存します。旧`get_statement`と`query_tagged_statements`はProtocol 2非互換エラーを返します。詳細は[構造化拡張の契約](STRUCTURED_KNOWLEDGE.ja.md)を参照してください。

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
- dreamは`remembers` Claimのconfidenceを段階的に減衰し、基準未満で`active`から`stale`へ変更します。
- Streamable HTTPによるMCPサーバーとしてProtocol 2のToolを提供します。stdio Transportは0.18.0で廃止しました。
- 本文を持たない入力単位と派生Claimへのタグ付与、AND/OR検索、後付け・解除、改名・別名・統合に対応します。[知識タグ仕様](KNOWLEDGE_TAGS.ja.md)を参照してください。
- MCP Tool呼び出しの利用状況を専用DB表へ自動記録します。引数と応答本文は保存しません。保持期間は自動設定されず、利用者がDBを管理します。

## 現在の開発版MSI（ユーザー権限のトレイ常駐）

現在のソースはユーザー単位のトレイ常駐アプリを生成します。この開発版パッケージの公開ダウンロードリンクは本書では案内していません。[開発手順](DEVELOPMENT.ja.md#msi生成)または[インストーラービルド手順（英語）](installer/README.md)から`Kotodama-0.21.0-user-x64.msi`を作成します。

インストール先は`C:\Kotodama`です。`bin`にプログラム、`config`に唯一の設定ファイル、`data`にSQLite DB、`logs`に日別ログを配置します。初回の保存先権限設定と旧サービス版の撤去は[Windowsインストール](INSTALLATION.ja.md)を参照してください。この初回準備には管理者権限が必要です。

準備後は通常ユーザーのPowerShellで導入・更新します。

```powershell
msiexec.exe /i "Kotodama-0.21.0-user-x64.msi" /norestart
& C:\Kotodama\bin\Kotodama.Tray.exe
```

ユーザー単位MSIはHKCU Runへトレイのログイン時起動を登録し、ユーザーPATHを設定します。トレイから状態確認、設定・ログの表示、再起動、終了を操作できます。更新前にトレイを終了し、更新後に起動します。ログイン前・ログアウト後は稼働しません。`C:\Kotodama\config\kotodama.json`は未作成の場合のみ配置し、更新・削除時にも設定ファイルと非空のデータディレクトリを保持します。

## 公開済み旧版MSI（0.20.0、Windowsサービス方式）

次のダウンロードは従来のWindowsサービス版であり、現在の開発版トレイアプリは含みません。

[Kotodama-0.20.0-x64.msi](https://github.com/katsushoe/Kotodama/releases/download/v0.20.0/Kotodama-0.20.0-x64.msi)

```powershell
Get-FileHash .\Kotodama-0.20.0-x64.msi -Algorithm SHA256
Start-Process msiexec.exe -Verb RunAs -Wait `
  -ArgumentList '/i "Kotodama-0.20.0-x64.msi" /norestart'
```

この旧MSIは`C:\Kotodama`へPC全体用に導入してWindowsサービスを登録するため、管理者権限で実行します。開発版トレイMSIへ切り替える場合は上記の移行手順を使用し、両方を同時に導入しないでください。

## Claude Desktop Extension（廃止）

0.18.0でstdio MCPサーバーを廃止したため、Claude Desktop Extension（DXT）の配布を終了しました。導入済みの場合は、Claude Desktopの`Settings > Extensions`でKotodama拡張を無効化または削除してください。旧版の拡張がMSI版と同じDBを開くと、移行済みスキーマを変更するおそれがあります。Claude Codeからは常駐HTTPサーバーへ接続します。

## 公開済み旧版ZIP（0.20.0）

[Kotodama-0.20.0-win-x64.zip](https://github.com/katsushoe/Kotodama/releases/download/v0.20.0/Kotodama-0.20.0-win-x64.zip)をダウンロードし、書き込み可能な任意の場所へ展開します。

```powershell
Get-FileHash .\Kotodama-0.20.0-win-x64.zip -Algorithm SHA256
Expand-Archive .\Kotodama-0.20.0-win-x64.zip -DestinationPath C:\Tools\Kotodama
& C:\Tools\Kotodama\Kotodama.exe
```

これは旧版0.20.0のZIPです。現在の開発版トレイアプリを含むZIPの生成は[開発手順](DEVELOPMENT.ja.md#portable-zip生成)を参照してください。

ZIPは自己完結型で、別途.NET Runtimeを必要としません。Windowsへの製品登録、`PATH`変更、自動Upgradeは行いません。更新時は展開先の`data`ディレクトリを保持してください。

## ソース配布を使う場合

Windows x64でWindows Desktop参照を含む.NET 10 SDKを導入し、[初回準備](INSTALLATION.ja.md#保存先と初回準備)で保存先への書き込み権限を確保します。その後、現在の開発ブランチを取得してビルドします。

```powershell
git clone --branch develop https://github.com/katsushoe/Kotodama.git
Set-Location Kotodama
dotnet restore Kotodama.slnx
dotnet build Kotodama.slnx -c Release --no-restore
New-Item -ItemType Directory -Force C:\Kotodama\config | Out-Null
if (-not (Test-Path C:\Kotodama\config\kotodama.json)) { Copy-Item installer\kotodama.json C:\Kotodama\config\kotodama.json }
dotnet run --project src\Kotodama.Tray -c Release --no-build
```

## 起動時の注意

現在の開発版MSIのトレイ実行ファイルは次の場所です。

```text
C:\Kotodama\bin\Kotodama.Tray.exe
```

KotodamaはStreamable HTTP専用のMCPサーバーです。設定は`C:\Kotodama\config\kotodama.json`だけから読み、環境変数や別の設定ファイルは読みません。既定の待受は`http://127.0.0.1:39280`、MCP接続先は`http://127.0.0.1:39280/mcp`です。MSI版はユーザー権限の`Kotodama.Tray.exe`でトレイに常駐し、ログイン時に起動します。導入・移行は[インストール手順](INSTALLATION.ja.md)を参照してください。stdio Transportは0.18.0で廃止しました。

既定のデータベースは次の場所です。

```text
C:\Kotodama\data\kotodama.db
```

## ソースからの起動

```powershell
dotnet run --project src/Kotodama.Tray -c Release --no-build
```

トレイとそのHTTPホストが起動します。HTTPホストだけを起動する場合は`dotnet run --project src/Kotodama -c Release --no-build`を使用します。同じ接続先ではトレイと単独ホストを同時に起動しないでください。どちらも唯一の設定ファイルを読み、ソースからの起動だけではログイン時起動やAIクライアントを登録しません。

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

- HTTP常駐時はdreamを既定3600秒間隔で実行します。間隔やその他の設定は`C:\Kotodama\config\kotodama.json`で変更できます。
- `kotodama backup <出力先.db>`でSQLiteオンラインバックアップを作成できます。
- MSI版のログは`C:\Kotodama\logs\kotodama-YYYYMMDD.log`へ日別保存します。
- Claim物理削除とRelationType削除は取り消せません。RelationTypeは使用中の場合、削除を拒否します。
