# MCP Tool仕様

Kotodamaはstdio／Streamable HTTP Transportで18個のToolを提供します。プロパティ名はJSONではcamelCaseを使用します。

## 管理操作

- `reactivate_claim`: 撤回またはstaleのClaimをactiveへ戻し、再確認日時を更新します。
- `delete_claim`: Claimを物理削除します。取り消せません。
- `update_relation_type`: RelationTypeの名称、カテゴリ、鮮度規則等を更新します。方向性は変更しません。
- `delete_relation_type`: 未使用のRelationTypeを物理削除します。使用中は`in_use`を返します。

## Server InstructionsとPrompt

MCP初期化時に、Kotodamaを永続的な構造化知識として利用するためのServer Instructionsを返します。対応クライアントはこの指針をAIのコンテキストへ追加できます。指針には、回答前の検索、再利用可能な事実だけの登録、Entityの重複確認、競合Claimの保持、Source・確信度・時点の記録、機密情報の登録前確認が含まれます。「覚えて」「記憶して」「今後参照して」等の明示依頼では、組み込みメモリやファイル作成より`remember_knowledge`を優先するよう指示します。

`prompts/list`と`prompts/get`により、次のMCP Promptも提供します。

| Prompt | 用途 |
|---|---|
| `use_kotodama` | 会話中の知識検索と、安全なClaim登録の手順をAIへ渡す |

Promptは利用者またはクライアントが選択して使用します。Server InstructionsとPromptの採用方法はMCPクライアントに依存し、Kotodamaへ接続しただけで会話が自動保存されることを保証しません。

| Tool | 主な入力 | 出力・状態変化 |
|---|---|---|
| `get_version` | なし | `{name, version}`。副作用なし |
| `create_entity` | `input` | Entityを追加し`EntityRecord`を返す |
| `get_entity` | `id` | Entityまたは`null` |
| `search_entities` | `query`, `limit` | canonical name部分一致。1～200件 |
| `create_relation_type` | `input` | RelationTypeを追加しIDを返す |
| `propose_claim` | `candidate` | 検証後にRelation、Source、Claimを原子的に保存 |
| `remember_knowledge` | `input.text`、任意のnamespace・確信度・Source・時点 | 自然文をユーザーが主張したStatementとして一括保存。完全一致Claimは再確認 |
| `retract_claim` | `claimId` | active Claimをretractedへ変更 |
| `query_claims` | 任意の検索条件、`includeRetracted`、`includeStale` | Claim一覧。空配列はunknown |
| `query_relations` | Entity、RelationType | 関連Claim一覧 |
| `get_neighbors` | `entityId` | Entityへ接続するClaim一覧 |
| `get_knowledge_context` | `entityId` | 現在時点で有効なClaim一覧 |
| `create_event` | `input` | Event EntityとEvent行を追加 |
| `run_dream` | なし | `remembers`のconfidenceを段階減衰し、基準未満または期限超過Claimをstaleへ変更 |

## 代表的な入力

### create_entity

```json
{
  "input": {
    "canonicalName": "佐藤",
    "className": "Person",
    "namespace": "project:sample",
    "metadata": "{}"
  }
}
```

### create_relation_type

```json
{
  "input": {
    "canonicalName": "works_at",
    "category": "organizational",
    "kind": "Directed",
    "allowStrength": false,
    "freshnessPolicy": "Periodic",
    "refreshAfterSeconds": 86400
  }
}
```

`kind`は`Directed`または`Symmetric`、`freshnessPolicy`は`Permanent`、`Periodic`、`Volatile`です。`Periodic`と`Volatile`は、どちらも`refreshAfterSeconds`によりdream対象となり、現在の動作に差はありません。

### propose_claim

```json
{
  "candidate": {
    "subjectId": 1,
    "objectId": 2,
    "relationType": "works_at",
    "polarity": "Positive",
    "confidence": 0.9,
    "assertionType": "reported",
    "observedAt": "2026-08-25T00:00:00Z",
    "source": {
      "sourceType": "official_document",
      "uri": "https://example.invalid/document",
      "reliability": 0.95
    }
  }
}
```

`confidence`、`attributionConfidence`、`strength`は0以上1以下です。`strength`はRelationTypeの`allowStrength`がtrueの場合だけ指定できます。`validTo`は`validFrom`より前にできません。参照EntityまたはRelationTypeが存在しない場合は`status: rejected`です。

### remember_knowledge

```json
{
  "input": {
    "text": "このプロジェクトの定例バックアップは毎週金曜日の18時に実行します。",
    "namespace": "global",
    "confidence": 1
  }
}
```

`text`はユーザーが提示した事実を改変せず渡します。Kotodamaは`Conversation user`からStatement Entityへの`remembers` Claimとして、一つのトランザクションで保存します。同じnamespaceと完全一致する非撤回Claimがある場合は`already_stored`を返し、Claimを重複登録せずconfidence、状態、確認日時を回復します。このToolは自然文の意味解析を行わず、原文を保持します。

### query_claims

```json
{
  "entityId": 1,
  "relationType": "works_at",
  "validAt": "2026-08-25T00:00:00Z",
  "includeRetracted": false,
  "includeStale": false
}
```

`includeRetracted`と`includeStale`の既定値はfalseです。`validAt`を省略すると有効期間による絞り込みを行いません。

### create_event

`objectId`または`objectValue`の一方が必要です。Event自体は`className: Event`のEntityとして作成されます。

## 業務エラー

`propose_claim`と`retract_claim`の業務上の不成立はMCPプロトコルエラーではなく、`OperationResult`として返します。

| status | 意味 |
|---|---|
| `accepted` | Claim保存成功 |
| `rejected` | 入力規則、Entity、RelationType等の検証不成立 |
| `retracted` | 論理撤回成功 |
| `not_found` | 撤回対象となるactive Claimがない |

DBを開けない、スキーマ初期化失敗等はサーバー境界で標準エラーへ記録され、終了コード1で停止します。
