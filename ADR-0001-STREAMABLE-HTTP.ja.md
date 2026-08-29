# ADR-0001: Streamable HTTP Transport

## Status

Accepted

## Context

KotodamaはMCP stdioだけを提供していました。複数クライアントから同じ常駐プロセスとSQLite Knowledge Graphを利用するため、Streamable HTTPが必要です。同一端末上の意図しないプロセスからのアクセスも制限できるよう、任意の共有token認証を追加します。

## Decision

- 既定の`stdio`を維持し、`KOTODAMA_TRANSPORT=http`を指定した場合だけStreamable HTTPで起動します。
- HTTP URLは`KOTODAMA_HTTP_URL`で必須指定し、認証の有無にかかわらずloopback hostだけを許可します。
- `KOTODAMA_HTTP_TOKEN`が設定されている場合、`/mcp`への全要求でBearer token認証を必須とします。未設定時は後方互換のため認証なしで動作します。
- MCP endpointは`/mcp`で固定します。
- HTTP Transportはstatelessとします。Kotodamaの状態はSQLiteにあり、server-to-client request、購読、クライアント単位のメモリ状態を必要としないためです。
- legacy SSEは提供しません。

## Alternatives

- HTTPを既定にする案は、既存stdioクライアントとの互換性を損なうため採用しません。
- LANへ認証なしで公開する案は、第三者がClaimを読み書きできるため採用しません。
- OAuth 2.1を組み込む案は、単一端末の共有プロセス用途に対して認可サーバーの導入とクライアント登録が過大なため採用しません。
- stateful HTTPは、現行機能にSession状態が不要で運用負荷だけが増えるため採用しません。

## Impact

ASP.NET Coreと`ModelContextProtocol.AspNetCore`が実行依存へ追加されます。HTTPモードではKotodamaを常駐起動する必要があります。stdioのTool、Prompt、Server Instructions、DB動作は変更しません。

## Security

loopback制限だけでは同一端末上の他プロセスを認証しません。必要な環境では十分に長いランダムな`KOTODAMA_HTTP_TOKEN`を設定し、ログや設定ファイルへ平文で記録しません。比較は固定時間で行います。共有tokenは利用者別権限を提供しないため、機密性の異なる利用者が同じ端末を共有する場合はOSユーザーを分離します。LAN、Internet、コンテナ外部へ公開するにはOAuth 2.1等の認可、HTTPS、Host検証を別途設計します。

## Operations

HTTPモードの起動、停止、再起動は利用者側のプロセス管理で行います。Windows Service登録は本変更に含めません。稼働確認は`/mcp`へのMCP初期化と`get_version`で行います。

## Implementation and verification

Transport設定の単体テストに加え、Bearer tokenの正常系、未指定、誤指定と、Streamable HTTPで初期化、Tool discovery、Server Instructions、Prompt、read、write、business errorを結合テストします。README、設定、運用、開発文書を本ADRへ整合させます。
