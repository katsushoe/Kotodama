# Knowledge Model

## 概念

- `Entity`: 人、組織、物、概念、Event等の識別対象です。
- `RelationType`: Relationの意味、方向、strength可否、鮮度規則を定義します。
- `Relation`: 2つのEntity間の構造です。有向または対称です。
- `Claim`: Relationについて、誰が何をどの確からしさで主張したかを表します。
- `Source`: Claimの根拠となる文書、発言、URL等です。Knowledgeの主語とは別です。
- `Event`: 発生日時、actor、action、objectを持つ独立したEntityです。
- `SimilarityGroup`: グループ固有のthresholdとmember_of所属で類似クラスタを表すEntityです。

構造化保存、Source経由のStatement追跡、similar_toとequalsの意味論は[構造化拡張の契約](STRUCTURED_KNOWLEDGE.ja.md)を参照してください。

## Claimの状態

```text
active --retract_claim--> retracted
active -----dream------> stale
retracted --reactivate_claim--> active
stale -----reactivate_claim--> active
```

- `active`: 現在の検索対象です。
- `retracted`: 明示的に撤回済みです。既定検索から除外されます。
- `stale`: 現在性またはconfidenceの基準を下回り、既定検索から除外されています。falseやretractedではありません。
- `reactivate_claim`は`retracted`または`stale`を`active`へ戻し、指定された再確認日時を`last_confirmed_at`と`updated_at`へ保存します。再確認日時を省略した場合は実行日時を使用します。すでに`active`のClaimまたは存在しないClaimは更新しません。
- `remembers` Claimは30日間再確認されないごとにconfidenceが80%へ減衰し、0.2未満で`stale`になります。同一文の再入力は既存Claimを再確認し、confidenceを回復します。

## Open World

検索結果が空の場合は「該当するClaimを保持していない」という意味です。Relationがfalseであることを意味しません。falseを表現する場合は`polarity: Negative`のClaimを明示的に保存します。ただし厳密な同値関係の`equals/canonical_of`はNegativeを許可しません。

## 競合情報

同じRelationへ複数のClaimを保存できます。肯定と否定、異なるSource、異なるconfidenceを上書きせず保持します。利用者はSource、時点、confidence等を参照して判断します。

## 有向Relationと対称Relation

- `Directed`: `subject_id`から`object_id`への向きを保持します。
- `Symmetric`: Entity IDの小さい側をA、大きい側をBとして正規化します。入力順を反転しても同じRelationを再利用します。
- 自己関係の明示Claimは`equals`だけが許可します。同値集合の反射性は、自己Claimが未登録でも`get_equivalent_entities`により保証します。

## 時間

日時はUTCへ変換してISO 8601形式で保存します。`valid_from`は境界を含み、`valid_to`は境界を含みません。したがって`valid_at == valid_to`のClaimは検索対象外です。
