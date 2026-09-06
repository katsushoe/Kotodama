# SecurityとPrivacy

## 原文の境界

`remember_knowledge.input.statement`は語彙抽出中のメモリだけで扱います。永続化層には本文を渡さず、`knowledge_inputs`、順序とoffsetを持たない`input_terms`、Entity、Relation、Claim、検証済みSource Metadataだけを書き込みます。応答、例外、通常ログにも本文を含めません。

Credentialらしい入力は処理前に要求全体を拒否します。Entity、Event、Source Metadataは原子的な識別子・短い名詞句・非文字列scalarへ制限します。http/https Source URIはoriginへ正規化し、userinfo、query、fragmentを拒否します。

## Threat Model

保護対象はSQLite DB、WAL、SHM、Backup、Log、Crash Dump、Memory Dumpです。新規処理は本文を永続層へ書きません。旧Schemaの移行ではTableを再構築し、WALを切り詰め、journalを一時的にDELETEへ変更して`VACUUM`後にWALへ戻します。既存の旧DB・Backupは自動削除しません。実測した対象について利用者が明示承諾した場合だけ破棄します。

処理中のMemoryとCrash Dumpには入力が残る可能性があります。また、順序を除いた語彙集合、Entity、Relation、Claimから内容を推測できる残余Riskがあります。OSのメモリ保護、Crash Dump制限、DBとBackupのアクセス制御を併用してください。
