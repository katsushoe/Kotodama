# ADR-0001: Streamable HTTP Transport

## Status

Accepted

## Context

KotodamaはMCP stdioだけを提供していました。複数クライアントから同じ常駐プロセスとSQLite Knowledge Graphを利用するため、Streamable HTTPが必要です。一方、v0.2にはHTTP認証、TLS終端、アクセス制御がありません。

## Decision

- 既定の`stdio`を維持し、`KOTODAMA_TRANSPORT=http`を指定した場合だけStreamable HTTPで起動します。
- HTTP URLは`KOTODAMA_HTTP_URL`で必須指定し、認証を実装するまではloopback hostだけを許可します。
- MCP endpointは`/mcp`で固定します。
- HTTP Transportはstatelessとします。Kotodamaの状態はSQLiteにあり、server-to-client request、購読、クライアント単位のメモリ状態を必要としないためです。
- legacy SSEは提供しません。

## Alternatives

- HTTPを既定にする案は、既存stdioクライアントとの互換性を損なうため採用しません。
- LANへ認証なしで公開する案は、第三者がClaimを読み書きできるため採用しません。
- stateful HTTPは、現行機能にSession状態が不要で運用負荷だけが増えるため採用しません。

## Impact

ASP.NET Coreと`ModelContextProtocol.AspNetCore`が実行依存へ追加されます。HTTPモードではKotodamaを常駐起動する必要があります。stdioのTool、Prompt、Server Instructions、DB動作は変更しません。

## Security

loopback制限は同一端末上の他プロセスを認証するものではありません。機密性の異なる利用者が同じ端末を共有する環境ではHTTPモードを使用しません。LAN、Internet、コンテナ外部へ公開する前に認証、認可、HTTPS、Host検証を設計・実装します。

## Operations

HTTPモードの起動、停止、再起動は利用者側のプロセス管理で行います。Windows Service登録は本変更に含めません。稼働確認は`/mcp`へのMCP初期化と`get_version`で行います。

## Implementation and verification

Transport設定の単体テストに加え、Streamable HTTPで初期化、Tool discovery、Server Instructions、Prompt、read、write、business errorを結合テストします。README、設定、運用、開発文書を本ADRへ整合させます。
