# Knowledge Model

## 概念

- `Entity`: 人、組織、物、概念、Event等の識別対象です。
- `RelationType`: Relationの意味、方向、strength可否、鮮度規則を定義します。
- `Relation`: 2つのEntity間の構造です。有向または対称です。
- `Claim`: Relationについて、誰が何をどの確からしさで主張したかを表します。
- `Source`: Claimの根拠となる文書、発言、URL等です。Knowledgeの主語とは別です。
- `Event`: 発生日時、actor、action、objectを持つ独立したEntityです。

## Claimの状態

```text
active --retract_claim--> retracted
active -----dream------> stale
retracted --reactivate_claim--> active
stale -----reactivate_claim--> active
```

- `active`: 現在の検索対象です。
- `retracted`: 明示的に撤回済みです。既定検索から除外されます。
- `stale`: 現在性の確認期限を超えています。falseやretractedではありません。
- `reactivate_claim`は`retracted`または`stale`を`active`へ戻し、指定された再確認日時を`last_confirmed_at`と`updated_at`へ保存します。再確認日時を省略した場合は実行日時を使用します。すでに`active`のClaimまたは存在しないClaimは更新しません。

## Open World

検索結果が空の場合は「該当するClaimを保持していない」という意味です。Relationがfalseであることを意味しません。falseを表現する場合は`polarity: Negative`のClaimを明示的に保存します。

## 競合情報

同じRelationへ複数のClaimを保存できます。肯定と否定、異なるSource、異なるconfidenceを上書きせず保持します。利用者はSource、時点、confidence等を参照して判断します。

## 有向Relationと対称Relation

- `Directed`: `subject_id`から`object_id`への向きを保持します。
- `Symmetric`: Entity IDの小さい側をA、大きい側をBとして正規化します。入力順を反転しても同じRelationを再利用します。

## 時間

日時はUTCへ変換してISO 8601形式で保存します。`valid_from`は境界を含み、`valid_to`は境界を含みません。したがって`valid_at == valid_to`のClaimは検索対象外です。
