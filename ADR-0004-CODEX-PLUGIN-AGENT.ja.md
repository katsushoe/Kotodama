# ADR-0004: Codexプラグインと知識整理Agentを分離して提供する

## Status

Accepted

## Context

KotodamaのMCP ToolだけをCodexへ登録しても、会話から知識候補を抽出して書き込むかどうかは親Agentの判断に依存します。CodexプラグインはSkillとMCP接続を配布できますが、ユーザースコープのカスタムAgentは`~/.codex/agents`で管理され、プラグインmanifestとは別の設定です。

## Decision

Kotodama CodexプラグインはMCP接続と`kotodama-knowledge` Skillを提供します。`kotodama configure codex`は、これらとは別に`kotodama-curator` Agent定義をユーザースコープへ原子的に登録します。Stop HookはAgentが利用可能なら知識整理を委譲し、利用不能なら親Agentが同じ確認を実行するよう指示します。

プラグイン、Agent、Hookは既存のKotodama MCP Toolを呼び出すだけとし、知識抽出用の外部LLM APIをKotodamaへ追加しません。

## Alternatives

- プラグインだけで自動登録する案: Skillの自動選択だけでは毎ターンの実行を保証できないため不採用です。
- Hookから別LLM APIを直接呼ぶ案: 認証、費用、モデル管理がKotodamaへ混入するため不採用です。
- 親Agentだけで知識整理する案: 会話Contextを知識整理結果で増加させるため、Agent利用可能時の第一選択にはしません。

## Impact

- `plugins/kotodama`をCodexプラグイン正本とします。
- `configure codex`と`unconfigure codex`はMCP、Hook、Scheduled Taskに加えてAgent定義も管理します。
- Agentを利用できないCodex環境でも、Hookから親Agentへ同じ処理を要求するフォールバックを維持します。

## Security conditions

- Agentは既存Kotodama MCP Toolだけを使用し、会話全文、秘密情報、認証情報、未確認の推測を保存しません。
- 個人情報または識別が曖昧な知識は、書き込み前に利用者確認を要求します。
- PluginのMCP endpointは既存のloopback Streamable HTTPだけを参照します。

## Operational conditions

- Agent定義は`kotodama-curator.toml`だけを製品所有ファイルとして更新・削除します。
- Plugin導入後は新しいCodex TaskでSkillとMCP Toolを確認します。
- Kotodama HTTP常駐プロセスが停止している場合、PluginのMCP接続は利用できません。

## Implementation, tests, and documentation

- Plugin manifest、MCP設定、Skill、Agent templateを版管理します。
- Plugin validator、Skill validator、構造テスト、Agent設定の原子的更新・削除テストを実行します。
- README、Install、Development文書へ構成、導入、制約を記載します。
