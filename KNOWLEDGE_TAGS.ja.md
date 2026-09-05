# 知識タグ設計・API仕様

## 設計判断

- Status: 採用。CR-2026-09-05-knowledge-tagsへの対応です。
- Context: 本文に作品名を含まない保存文とClaimを、namespace内の共通分類から取得します。
- Decision: Tag ID、正規名・別名の共有索引、Statement/Claimとの多対多関連をSQLiteに追加します。保存文は既存Statement Entityです。
- 代替案: 文字列の各レコードへの重複保存は改名・統合の不整合を招くため不採用です。汎用Relationだけの分類は直接検索と別名管理を提供しないため不採用です。
- 影響範囲: 保存トランザクション、追加MCP Tool、サービス接続CLI、DB初期化、テスト、利用文書です。
- セキュリティ条件: namespaceは分類境界であり認可機構ではありません。既存Transport認証を維持し、タグ・対象のnamespace不一致は拒否します。SQL値はパラメーター化します。
- 運用条件: 本番データの一括変更はdry-runで件数を確認し、expectedCountを指定して実行します。サービスの更新・リリースは別作業です。

## 正規化とID

名前はUnicode NFKC、前後のUnicode空白除去、Invariant uppercaseを検索キーに使います。表示名はNFKCと前後空白除去までです。内部空白、かな・カナ、異なる綴りは自動同一視せず別名で扱います。空文字・制御文字・128 UTF-16文字超過・不正Unicodeは拒否します。namespaceは既存の完全一致を維持します。

正規名と別名はnamespace内の同じ一意索引を使います。改名はIDを維持し旧名を別名として残します。統合は同一namespaceだけに限定し、全関連と別名を移します。旧IDは削除せず統合先へ解決されます。再統合でも関連を重複作成しません。

## 保存と継承

`remember_knowledge.input.tags`は任意の名前配列です。省略・空配列は従来どおりです。最大100件、正規化後に重複排除します。タグを再利用または作成し、Statementとremembers Claim、当該要求の抽出Claimへ原子的に付与します。一般概念EntityやEventには付与しません。

由来はStatementで`remember`、保存時のClaimで`inherited`（sourceStatementId付き）、後付けで`manual`です。付与履歴を時系列監査する機能ではありません。構造エラーによるrejectedはタグも保存せず、最終fallbackではStatementとremembers Claimだけに付与します。既存Statementの再保存は追加方式で、既存タグを消しません。

## Tool契約

全JSONプロパティはcamelCaseです。追加Toolは以下の8個です。

| Tool | 入力 | 出力 |
|---|---|---|
| create_tag | name, entityNamespace="global" | TagRecord（既存名・別名は再利用） |
| list_tags | entityNamespace="global", afterId=0, limit=50 | ID順TagRecord一覧（統合済み含む） |
| rename_tag | tagId, name, entityNamespace="global" | 同じIDのTagRecord |
| add_tag_alias | tagId, alias, entityNamespace="global" | TagRecord |
| merge_tags | sourceTagId, targetTagId, entityNamespace="global" | 統合先TagRecord |
| set_knowledge_tags | input | matchedCount, changedCount, dryRun |
| query_tagged_statements | input | statementとtags由来一覧 |
| query_tagged_claims | input | claimとtags由来一覧 |

TagRecordはid, name, namespace, aliases, mergedIntoIdです。検索のtagsはtagId, name, origin, sourceStatementIdを返します。

検索input: `tags`（名前）、`tagIds`（ID）の少なくとも一方、`tagMatch: "any" | "all"`（既定any）、`namespace`（既定global）、`limit`（1～200、既定50）、`afterId`（既定0）。名前とIDは同じ候補集合へ合成し、別名・統合先の解決後に重複排除します。unknown名はanyで無視、allで全体を不一致にします。空条件はエラーです。ID昇順のカーソルページングです。

Claim検索はさらに`includeRetracted=false`, `includeStale=false`, `validAt=null`を指定できます。Statement検索はClaim状態と独立しています。タグの完全一致だけで検索し、本文の部分一致は行いません。

set_knowledge_tags.input: `targetKind: "statement" | "claim"`, `tagIds`（1～100個）、`targetIds`（1～200個）または`knowledgeSubjectId`のいずれか一方、`namespace="global"`, `remove=false`, `dryRun=true`, `expectedCount`。対象件数は対象レコード数、変更件数は実際に関連を変更した対象レコード数です。実行時はexpectedCount必須で、同一トランザクション内の対象件数が一致しないと全体を拒否します。単件でも同じ契約です。解除は指定対象とタグの全由来を削除します。他の保存文・Claimへ連鎖解除しません。後付けは指定対象だけに適用されます。

## エラーと移行

入力不正、存在しないID、namespace不一致、別タグと名前衝突、expectedCount不一致はArgumentException系で拒否し、MCP境界でMcpExceptionへ変換してisErrorを返します。必須引数欠落・型不一致などSDKの引数バインド時の失敗はプロトコルエラーになる場合があります。DB障害・キャンセルは伝播し、部分保存しません。CLIはMCP結果をJSON出力し、isErrorまたは接続・入力失敗を非0終了コードにします。

更新前に既存のbackup操作でDBを退避します。初期化時に追加テーブルと索引を冪等作成し、既存行を書き換えません。旧クライアントの保存・検索はそのまま利用できます。初期化の再実行、既存データ保持、バックアップ復元後の再初期化を検証対象とします。旧バイナリへ戻す場合は旧バイナリで新規タグを操作できません。完全なロールバックは更新前バックアップへ戻します。

## 利用例

```json
{"input":{"statement":"主人公は北の城で育った。","entities":[],"relations":[],"reason":"原文のみ保存","tags":["ミルラッド年代記"]}}
```

```json
{"input":{"tags":["ミルラッド年代記","人物設定"],"tagMatch":"all"}}
```

```json
{"input":{"targetKind":"statement","knowledgeSubjectId":1,"tagIds":[1],"dryRun":true}}
```

dry-runのmatchedCountを確認して同じ条件に`dryRun:false, expectedCount:<確認件数>`を指定します。

CLIは稼働中HTTPサービスへ接続する`Kotodama call <tool> <arguments.json>`です。引数ファイルはMCPと同じJSONオブジェクトです。接続先は既存のKOTODAMA_HTTP_URL、認証はKOTODAMA_HTTP_TOKENを使います。全Toolを同じ方法で呼び出せます。DBを直接操作しません。
