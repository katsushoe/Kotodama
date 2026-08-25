namespace Kotodama;

/// <summary>KotodamaのMCP Transportです。</summary>
internal enum McpTransport
{
    Stdio,
    Http,
}

/// <summary>起動時のMCP Transport設定です。</summary>
internal sealed record ServerSettings(McpTransport Transport, Uri? HttpUrl)
{
    internal const string HttpPath = "/mcp";

    /// <summary>環境変数からTransport設定を読み取ります。</summary>
    internal static ServerSettings FromEnvironment() => Parse(
        Environment.GetEnvironmentVariable("KOTODAMA_TRANSPORT"),
        Environment.GetEnvironmentVariable("KOTODAMA_HTTP_URL"));

    /// <summary>Transport名とHTTP URLを検証します。</summary>
    internal static ServerSettings Parse(string? transportText, string? httpUrlText)
    {
        if (string.IsNullOrWhiteSpace(transportText) || transportText.Equals("stdio", StringComparison.OrdinalIgnoreCase))
        {
            return new(McpTransport.Stdio, null);
        }

        if (!transportText.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("KOTODAMA_TRANSPORT must be stdio or http.");
        }

        if (!Uri.TryCreate(httpUrlText, UriKind.Absolute, out var httpUrl))
        {
            throw new InvalidOperationException("KOTODAMA_HTTP_URL must be an absolute HTTP or HTTPS URL.");
        }

        if (httpUrl.Scheme != Uri.UriSchemeHttp && httpUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("KOTODAMA_HTTP_URL must use HTTP or HTTPS.");
        }

        if (!httpUrl.IsLoopback)
        {
            throw new InvalidOperationException("KOTODAMA_HTTP_URL must use a loopback host while authentication is unavailable.");
        }

        if (httpUrl.AbsolutePath != "/" || !string.IsNullOrEmpty(httpUrl.Query) || !string.IsNullOrEmpty(httpUrl.Fragment) || !string.IsNullOrEmpty(httpUrl.UserInfo))
        {
            throw new InvalidOperationException("KOTODAMA_HTTP_URL must contain only the scheme, loopback host, and port.");
        }

        return new(McpTransport.Http, httpUrl);
    }
}
