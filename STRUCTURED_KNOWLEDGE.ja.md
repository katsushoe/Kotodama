# remember_knowledge 構造化拡張の契約

この文書は入力・出力・意味論を定義します。実装方式と設計判断の理由は [設計ノート](STRUCTURED_KNOWLEDGE_DESIGN.ja.md) を参照してください。

## 入力

MCP引数は `input` オブジェクトです。`statement`、`entities`、`relations` を必須とします。従来の `input.text` のみの呼び出しはエラーになります。`namespace`、`confidence`、`source`、`observedAt`、`validFrom`、`validTo`、`event` は引き続き利用できます。`statement` は前後の空白を含めて保存します。

- `entities`: `key`、`canonicalName`、任意の `className`（既定Entity）、`entityId`、`metadata`。`key` は要求内で一意です。`entityId` 指定時は名称・class・namespaceが一致する既存Entityを参照します。未指定時は同一名称・class・namespaceを再利用します。既存Entityのmetadataは上書きしません。
- `relations`: `subject`、`object`（entitiesのkey）、`relationType`、任意の `polarity`（Positive）、`confidence`（1）、`strength`。予約語彙以外のRelationTypeは事前に `create_relation_type` で登録します。
- `tags`: 任意のタグ名配列。保存文と当該保存のClaimへ原子的に付与します。正規化、継承、検索・管理契約は[知識タグ仕様](KNOWLEDGE_TAGS.ja.md)を参照してください。
- 概念数の目安は2件以上、関係は1件以上です。これは件数の強制ではなく、空配列時の再入力案内です。上限は概念100件・関係200件です。
- どちらかが空なら、意図的ゼロ件の `reason` が必要です。非空の配列はその場合も保存対象です。両配列は `reason` があっても省略不可です。
- `retryCount` は初回0、再入力1～3です。呼び出し元が構造を修正して最大3回まで再入力します。3回目も構造エラーなら原文Statementとremembers Claimだけを保存します。概念・抽出関係・今回のEventは保存しません。
- statement、namespace、確信度・日時、配列の欠落、retryCountの範囲など基本契約の違反と、DB障害・キャンセルはフォールバック対象外です。構造エラーは未知RelationType、参照key不備、上限超過、関係制約違反などです。
- 必須入力の欠落はMCP引数バインド時のプロトコルエラーになる場合があります。構造検証の `ok:false` と区別し、入力契約を修正します。

```json
{
  "input": {
    "statement": "AとBは似ています。",
    "namespace": "sample",
    "entities": [
      { "key": "a", "canonicalName": "A" },
      { "key": "b", "canonicalName": "B" }
    ],
    "relations": [
      { "subject": "a", "object": "b", "relationType": "similar_to", "confidence": 0.9, "strength": 0.7 }
    ]
  }
}
```

## 保存・出力

原文、概念、抽出関係、指定Eventは全体として成功するか、全体が保存されないかのどちらかです。最終再入力の縮退だけは原文保存が成功します。成功した `reason` による省略はエラーではありません。

返却値は従来の `ok`、`status`、`subjectId`、`statementId`、`claimId`、`createdEntities`、`createdRelationType`、`eventId` に加え、以下を含みます。

- `structureStatus`: `structured`（構造あり）、`skipped`（理由付きゼロ件）、`fallback`（修正上限後の原文保存）、`rejected`（未保存）。内部の旧保存APIのみ `legacy`。
- `reason`: 省略理由、検証エラーまたは縮退理由。`ok:false/status:rejected` は入力を修正すべき結果です。返却IDは0であり、永続IDとして使えません。
- `entityIds`: 要求内keyと永続Entity IDの対応。
- `claimIds`: 抽出Claim ID。再確認したClaimも含みます。原文へのremembers Claimは従来の `claimId` です。

既存原文に構造を後から追加できます。同じ原文・関係・極性・strength・有効期間の非撤回抽出Claimは再確認し、重複させません。異なる出所の原文や矛盾するClaimは共存します。`status:stored` は新規原文・概念・抽出Claimが保存された場合、`already_stored` はそれらの追加がなかった場合です。既存Eventの扱いは従来どおりです。

抽出Claimの `sourceId` はSource IDです。Entity IDとの混用はしません。Source経由の `sourceStatementId` を `query_claims` 等のClaim返却値に含め、抽出元Statement Entityを参照できます。出典URI・信頼度など元のSource属性は保持します。

## 語彙と意味論

- `similar_to`: Symmetric、allowStrength=true、category=semantic、Periodic（再確認期限30日）。confidenceは判断への確信度、strengthは類似度で、どちらも有限な0～1。Negativeも保存できます。推移律は保証しません。期限を超えて `run_dream` が実行されるとstaleになります。
- `equals`: Symmetric、allowStrength=false、category=identity、Permanent。`canonical_of` は同じ語彙への別名です。Negativeは登録経路にかかわらず拒否します。
- 同値性は `get_equivalent_entities` が返す、現在有効なPositive equalsの反射・対称・推移閉包で定義します。存在するEntityは自分自身と同値です。撤回・stale・有効期間外の辺は使いません。物理統合も推論Claimの生成もしません。`query_claims` は明示されたClaimのみを返します。
- 上記の類似・同値関係は同一namespace内に限定します。既存のpropose_claimと同じ呼び出し権限で登録でき、追加の人間承認フラグはありません。
- `reactivate_claim`も現在の関係規則を再検証し、不正なNegative equalsなどを再有効化しません。
- `similar_to`、`equals`、`canonical_of`、`member_of` は予約語彙です。一般の作成・変更・削除APIで規則を変更できません。

## SimilarityGroup

`className:SimilarityGroup` のEntityと、概念からグループへ向かうDirected `member_of` Claimで表します。`member_of` のcategoryはclassificationです。グループへの所属は同じnamespaceの概念に限定し、グループの入れ子は認めません。一般のEntityを対象とするmember_ofの既存用途は維持します。

metadataは文字列として渡す固定JSON `{"threshold":0.5}` です。thresholdは有限の0～1とし、欠落・不正JSON・余分なプロパティ・型違反・範囲外は0.5へ正規化します。グループによりthresholdを変えられます。所属は明示Claimであり、similar_toの値から自動作成・自動分裂しません。

分裂は人間から指摘を受けた場合に限り、旧member_ofを `retract_claim` → 新しいSimilarityGroupを `create_entity` → `propose_claim` で所属を登録します。複数Toolにまたがる分裂は単一トランザクションではありません。

`merge_similarity_groups(groupAId,groupBId,canonicalName?)` は明示依頼に応じた統合です。別々の同一namespaceグループを指定します。新名称の省略時は `SimilarityGroup:<一意ID>`、指定時は未使用の名称に限ります。

`new_threshold = (threshold_A × memberCount_A + threshold_B × memberCount_B) / (memberCount_A + memberCount_B)`

件数は現在有効なPositive member_ofの一意メンバー数です。重複メンバーは各旧グループの重みに含め、新グループでは1件にします。両方空なら0.5です。旧グループへの未撤回所属Claimを撤回し、新グループへ貼り直します。旧EntityとClaim履歴を残します。自動統合はしません。

## 検索

`search_entities(query,limit=50,includeRelated=true)` は名前の部分一致を先に返し、残りの枠へ同一namespaceの類似・同値関係とグループ所属を辿った候補を返します。合計1～200件に制限し、名前一致だけで上限になれば展開しません。候補が多い場合は全クラスタの列挙を保証しません。`includeRelated=false` は従来の名前一致のみです。

各検索結果の `match` に直接一致 `name`、直接関係名、複数辺の `related_path` と、起点Entity ID・Claim ID経路を含めます。Negative、撤回、stale、有効期間外の辺は辿りません。A～B～Cという経路でCが関連候補になってもAとCの類似を主張しません。AとCのNegative類似Claimも矛盾なく保持できます。

## 既存データ

既存remembersログは自動抽出・移行しません。原文を構造付きで再入力した場合のみ補完します。スキーマは出典参照と反射的な同値関係に対応します。既存の予約語彙と規則が衝突する場合は初期化エラーとし、既存の意味を黙って変更しません。
