# dream仕様

HTTP常駐モードでは`KOTODAMA_DREAM_INTERVAL_SECONDS`（既定3600秒）ごとに自動実行します。MCP Toolの`run_dream`による手動実行も維持します。stdioモードは短命な接続を想定し、自動実行しません。

dreamはオンラインClaimを直接走査しながら逐次更新せず、接続ローカルの一時テーブルで更新候補を確定してから短いトランザクションで公開します。

## 対象

次の条件をすべて満たすClaimを一時テーブルへコピーします。

- Claimの状態が`active`
- RelationTypeの`freshness_policy`が`permanent`以外
- `refresh_after_seconds`が設定済み

基準日時は`last_confirmed_at`があればその値、なければ`observed_at`です。次の条件を満たす場合に`stale`候補となります。

```text
評価日時 - 基準日時 > refresh_after_seconds
```

境界と等しい場合はまだ`active`です。dreamはconfidence、polarity、Source、有効期間を変更しません。

## 処理手順

1. `CREATE TEMP TABLE dream_updates`で接続ローカルの一時テーブルを作ります。
2. 対象Claimを全件評価し、Claim ID、評価時点の`updated_at`、更新先状態、更新日時を保存します。
3. 即時書き込みトランザクションを開始します。
4. `status = active`かつ`updated_at`が退避時点と一致するClaimだけを`stale`へ更新します。
5. 全件更新できたらcommitし、一時テーブルを削除します。

SQLiteではテーブル自体をオンラインテーブルと物理交換するのではなく、更新候補を一時テーブルで隔離し、条件付き一括UPDATEをcommitすることで同等の原子性と競合保護を実現しています。

## 並行実行と障害

- 退避後にClaimが撤回・更新された場合、`updated_at`不一致によりdreamは上書きしません。
- 読み手はcommit前の`active`またはcommit後の`stale`を参照し、途中状態を参照しません。
- 2つのdreamが競合しても、同じ遷移を公開するのは1実行だけです。
- 退避後またはUPDATE後に失敗した場合、オンラインClaimは変更されないかロールバックされ、再実行できます。
- `finally`で一時テーブルを削除します。

## 実行方法

HTTP常駐モードでは、ホスト内のBackground Serviceが`KOTODAMA_DREAM_INTERVAL_SECONDS`（既定3600秒）ごとに実行します。不正値または0以下は既定値として扱います。stdioモードでは自動実行しません。

任意の時点でMCP Toolの`run_dream`を呼び出して手動実行できます。戻り値は評価対象件数`examined`、実際にstaleへ変更した件数`markedStale`、評価日時`evaluatedAt`です。同時実行は安全ですが、不要な競合を避けるため、定期実行元はHTTP常駐ホストだけにすることを推奨します。
