# MCP Tool仕様

Kotodamaはstdio Transportで13個のToolを提供します。プロパティ名はJSONではcamelCaseを使用します。

| Tool | 主な入力 | 出力・状態変化 |
|---|---|---|
| `get_version` | なし | `{name, version}`。副作用なし |
| `create_entity` | `input` | Entityを追加し`EntityRecord`を返す |
| `get_entity` | `id` | Entityまたは`null` |
| `search_entities` | `query`, `limit` | canonical name部分一致。1～200件 |
| `create_relation_type` | `input` | RelationTypeを追加しIDを返す |
| `propose_claim` | `candidate` | 検証後にRelation、Source、Claimを原子的に保存 |
| `retract_claim` | `claimId` | active Claimをretractedへ変更 |
| `query_claims` | 任意の検索条件 | Claim一覧。空配列はunknown |
| `query_relations` | Entity、RelationType | 関連Claim一覧 |
| `get_neighbors` | `entityId` | Entityへ接続するClaim一覧 |
| `get_knowledge_context` | `entityId` | 現在時点で有効なClaim一覧 |
| `create_event` | `input` | Event EntityとEvent行を追加 |
| `run_dream` | なし | 期限超過Claimをstaleへ変更 |

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

`kind`は`Directed`または`Symmetric`、`freshnessPolicy`は`Permanent`、`Periodic`、`Volatile`です。`Periodic`と`Volatile`の動作差はv0.1ではなく、どちらも`refreshAfterSeconds`によりdream対象になります。

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

### query_claims

```json
{
  "entityId": 1,
  "relationType": "works_at",
  "validAt": "2026-08-25T00:00:00Z",
  "includeRetracted": false
}
```

`includeRetracted`の既定値はfalseです。`stale`は除外されません。`validAt`を省略すると有効期間による絞り込みを行いません。

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
