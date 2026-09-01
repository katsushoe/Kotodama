# ADR-0005: 保存時の価値判定よりdreamによる段階的減衰を優先する

## Status

Accepted

## Context

会話時点で知識の長期利用価値を正確に判定することは難しく、厳格な保存判定は後から有用になる知識を失う。一方、候補を広く保存すると、一時的または低価値な知識が永続的に検索対象へ残る。

## Decision

根拠がある事実は、長期利用価値が不確かな場合も保存候補にできる。`remember_knowledge`が作る`remembers` Claimは30日周期の`periodic`とし、再確認されない期間ごとにconfidenceを80%へ減衰する。減衰後のconfidenceが0.2未満なら`stale`へ変更し、既定検索から除外する。物理削除はしない。

同じ知識が再入力された場合はClaimを重複作成せず、`active`へ戻し、confidenceと`last_confirmed_at`を回復する。秘密情報、認証情報、根拠のない推測、会話全文は従来どおり保存しない。

## Alternatives

- 保存時に長期価値を厳格判定する案: 将来価値の予測が難しく、必要な知識を保存できないため不採用です。
- 期限到達時に即stale化する案: 有用性が不確かな知識を一度の期限で検索対象外にするため不採用です。
- dreamで物理削除する案: 誤判定時に回復できず、Sourceと履歴の追跡性を損なうため不採用です。

## Impact

- `remembers`の既定鮮度は`permanent`から30日周期の`periodic`へ変わります。
- 既存の`permanent`かつ期限未設定の`remembers` RelationTypeは起動時に移行します。
- `run_dream`はstale件数に加えてconfidence減衰件数を返します。
- `query_claims`はstaleを既定除外し、`includeStale`で明示取得できます。

## Security conditions

- 段階減衰は保存時のプライバシー確認を代替しません。
- 秘密情報、認証情報、未確認の推測、会話全文は保存対象外です。
- dreamはClaimを物理削除せず、監査可能性と再確認経路を保持します。

## Operational conditions

- HTTP常駐モードの既定dream実行間隔は従来どおり1時間です。
- 30日経過につき1回だけ減衰し、同じ時点でdreamを再実行しても重複減衰しません。
- DB容量の縮小は別の明示的な削除・孤児回収機能で扱います。

## Implementation, tests, and documentation

- SQLite初期化時の移行、段階減衰、stale遷移、再確認、並行実行をテストします。
- DREAM、Knowledge Model、MCP Tool、README、SkillとAgent指示を本ADRへ整合させます。
