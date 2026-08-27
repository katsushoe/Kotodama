# ADR-0002 Claude Code Hooksによる知識利用の自動化

## Status

Accepted

## Context

MCP Server InstructionsとPromptの採用はクライアントに依存するため、Kotodama接続だけでは会話中の検索と知識登録を保証できません。一方、会話履歴をそのまま保存すると、構造化知識を保持するKotodamaの責務と機密情報保護に反します。

## Decision

Claude Codeのユーザースコープ`UserPromptSubmit` Hookから回答前の検索指針を追加し、`Stop` Hookから主エージェントへ永続化候補の確認を一度だけ要求します。知識の意味判断とMCP Tool呼び出しは主エージェントが行い、HookやKotodama DBは生の会話履歴を保存しません。

`Stop`入力の`stop_hook_active`がtrueの場合は継続要求を返さず、無限ループを防ぎます。Hook障害はClaude Codeの標準的な非ブロッキングHookエラーとして扱い、認証情報、秘密情報、推測、未承認の機微な個人情報は登録対象外とします。

Codexには同等の公開ライフサイクルHookがないため、Server InstructionsとMCP Promptをフォールバックとして維持します。

## 代替案と不採用理由

- 会話履歴をKotodamaへ自動保存する案: 構造化知識DBの責務と秘密情報保護に反するため不採用。
- Hook内で規則だけにより知識抽出する案: 意味判断が不十分で誤登録を避けられないため不採用。
- 全クライアントをローカルProxyで包む案: 導入負荷が大きく、現時点の要求を超えるため不採用。

## 影響範囲

`kotodama configure claude`と`configure all`はClaude MCP設定に加え、`~/.claude/settings.json`へKotodama固有Hookを追加します。unconfigureはKotodama固有Hookだけを削除し、他製品のHookを保持します。

## セキュリティ条件

Hookは会話をファイルやDBへ複製しません。知識登録は既存のKotodama MCP Toolと検証規則を経由します。Hookコマンドは固定の統合識別子で管理し、他のHookを変更しません。

## 運用条件

Claude CodeとKotodama MCP Serverが利用可能である必要があります。Hook設定の反映にはClaude Codeの再起動が必要な場合があります。

## 実装・テスト・利用者文書

Hook CLI、JSON設定の追加・重複防止・限定削除、Stop再入防止を自動テストします。インストール・運用文書には対応範囲、設定場所、Codexとの差異を記載します。
