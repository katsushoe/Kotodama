# ADR-0003: Claude DesktopへDXTでKotodamaを提供する

## Status

Accepted

## Context

Claude Code用プラグインのAgentとHookは、通常のClaude Desktop会話では利用できません。一方、Claude DesktopはDXTによるローカルMCPサーバーのインストールと、MCPのTool、Prompt、Resourceをサポートします。KotodamaのSQLite DBをDXT展開先へ置くと、Extension更新や削除により永続データを失う可能性があります。

## Decision

Windows x64自己完結型Kotodama stdioサーバーを含むDXTを配布します。DXTは既存のMCP Tool、`use_kotodama` Prompt、Server Instructionsをそのまま公開します。自然文を単一トランザクションで保存する`remember_knowledge` Toolを公開し、Server Instructionsでは明示的な記憶依頼がない場合も、直接裏付けられた永続的で再利用可能な事実を検出したターンで同Toolを呼ぶよう指示します。DBとログは利用者が選択したDXT外部のデータディレクトリへ保存します。

Claude DesktopではAgentやHookによる毎応答後の登録保証を仕様としません。`remember_knowledge`を含む知識登録はServer Instructions、MCP PromptおよびClaudeによるTool選択に基づくbest effortとします。

## Alternatives

- Claude Codeプラグインをそのまま使用する案: Claude Desktopは同じAgent／Hook実行環境を持たないため不採用です。
- DXT内へDBを保存する案: Extensionの更新・削除から永続データを分離できないため不採用です。
- Kotodama内へLLMを組み込む案: Provider認証、費用、モデル選択がKotodamaの知識保存責務へ混入するため不採用です。

## Impact

- `desktop-extension/manifest.json`を版管理し、Windows向けDXTをRelease成果物として生成します。
- `KOTODAMA_LOG_DIR`を追加し、DXTからDBとログの保存先を外部指定します。
- Claude Code用構成とMCPの業務機能は変更しません。

## Security conditions

- DXTはネットワーク上の別サービスではなく、Claude Desktopの子プロセスとしてstdioで起動します。
- DBとログに秘密情報を意図的に保存しません。知識登録時の既存の承認・選別規則を維持します。
- 利用者は信頼できる配布元のDXTだけをインストールし、公開Hashを照合します。

## Operational conditions

- DXTの更新前にデータディレクトリをバックアップします。
- Claude DesktopからExtensionを削除しても、外部データディレクトリは自動削除しません。
- DXTの自動知識登録は保証せず、必要な場合は`use_kotodama` Promptまたは`remember_knowledge` Toolを明示的に選択します。

## Implementation, tests, and documentation

- Build scriptで自己完結型publish、manifest同梱、DXT圧縮を行います。
- manifestの構造、外部データパス、Tool／Prompt動的検出宣言を自動テストします。
- README、インストール手順、設定、開発手順へDXTの作成・導入・制約を記載します。
