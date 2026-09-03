# 構造化知識の設計ノート / ADR

## Status

2026-09-04: 実装用の暫定判断。元の仕様まとめの未決9項目を、最終合意済み事項へ読み替えないこと。外部契約は [STRUCTURED_KNOWLEDGE.ja.md](STRUCTURED_KNOWLEDGE.ja.md) を正本とします。

## Context

原文Statementとremembersだけでは、概念を起点とする検索ができません。既存のEntity/Relation/Claim、SQLiteトランザクション、Source、dreamを活用して、呼び出し元が抽出済みの構造を一括登録できるようにします。

## Decision

サーバにLLMを組み込まず、抽出と修正リトライは呼び出し元の責務とします。サーバは必須配列と制約を検証し、retryCount=3で構造エラーなら原文のみ保存します。再入力回数は呼び出し側が申告する制御情報であり、権限・認証の代用にはしません。

既存の原文保存処理を共用し、構造部分にはsavepointを置きます。通常の構造エラーでは外側のトランザクションを破棄し、最終失敗ではsavepointまで戻して原文だけをコミットします。DB障害とキャンセルを入力エラーに変換しません。新APIで追加するEventも構造部分に含めます。

出典参照はsources.source_statement_idに格納し、Claim検索でJOINして返します。Source IDとEntity IDは区別します。重複判定はStatement・Relation・極性・strength・有効期間で行い、異なる主張を一括で上書きしません。既存Statementに対する構造追加を許可します。

同値性はrecursive CTEのUNIONで現在有効な辺の閉包を計算し、自己を起点に含めます。撤回時の削除伝播や推論Claimの出典問題を避けるため、閉包の物理保存は行いません。明示的な自己同値Claimを受け付けるため、symmetric_relationsのCHECKを `a<=b` に移行し、自己関係をequalsに限定する検証を置きます。

類似検索は名前一致から幅優先で辿り、訪問済みIDで循環・重複を防ぎます。返却上限までの候補とClaim経路を返し、類似性の推移を推論しません。グループ統合は明示的な1トランザクションです。分裂は既存ツールの組合せです。

## 未決9項目に対する暫定方針

1. equalsの物理統合: 行わず、検索で同値集合を返す。
2. threshold初期値: 明示値優先、未指定・不正値は0.5。
3. Claim生成権限: 既存の書き込み権限を継承し、LLMによる抽出結果を受け付ける。独自の承認フラグは追加しない。
4. 既存ログ: 保持し、再入力時のみ構造を補完する。
5. namespace跨ぎ: 類似・同値・SimilarityGroup所属では禁止する。
6. 統合ポリシー: 明示依頼に応じた操作のみ。定期スキャンは追加しない。
7. category: similar_to=semantic、equals=identity、member_of=classification。
8. 上限: entities=100、relations=200。
9. canonicalName: 明示名称。統合時の省略は `SimilarityGroup:<GUID>`。任意のcreate_entityでは従来どおり名称必須。

## 代替案と不採用理由

- サーバ側LLM抽出: 推論サービス・認証・費用という別の運用契約が必要なため今回採用しない。
- Entityの物理統合: 撤回・出典・既存参照を不可逆に変更しやすいため採用しない。
- 推論Claimの事前展開: 辺の撤回・stale化ごとに推論を再構築する必要があるため採用しない。
- 旧text-only MCP入力の暗黙受け入れ: entities/relations必須化を無効にするため採用しない。内部の旧保存メソッドは既存機能・回帰テスト用に維持する。

## 影響範囲とセキュリティ条件

変更はKotodamaのMCP入力、SQLite保存・検索、クライアント向け指針、テスト、利用者文書です。既存の接続・認証の範囲を拡張しません。出典はサーバで生成したStatement IDに結び、異なるnamespaceのEntity参照を拒否します。原文は機密の可能性があるためエラーログへ出力しません。SQL値はパラメータで渡します。

予約語彙の変更・削除・名前の置換を拒否し、alias経由でも同じ制約を適用します。既存の予約語彙が新しい規則と衝突するDBは黙って上書きせず、初期化時に停止します。

## 運用条件と対応方針

旧MCPクライアントはstatement/entities/relationsへ更新が必要です。Server Instructions、Prompt、配布するcurator/skill/hook指針を合わせます。インストール済みプラグインや稼働DBへの自動書き換えはこの開発変更に含めません。既存のEvent入力は保持します。

テストでは通常保存・全体ロールバック・最終縮退・再送・矛盾保持・出典追跡・同値閉包と撤回・Negative拒否・namespace境界・metadata既定値・加重統合・stale/期間による検索除外を確認します。MCP discoveryの必須schemaと新APIの呼び出しも検証します。
