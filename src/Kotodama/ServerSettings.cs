namespace Kotodama;

/// <summary>起動時のStreamable HTTP設定です。</summary>
internal sealed record ServerSettings(Uri HttpUrl, string? HttpToken)
{
    internal const string HttpPath = "/mcp";
    internal const string DefaultHttpUrl = "http://127.0.0.1:39280";

    /// <summary>環境変数からHTTP設定を読み取ります。</summary>
    internal static ServerSettings FromEnvironment() => Parse(
        Environment.GetEnvironmentVariable("KOTODAMA_TRANSPORT"),
        Environment.GetEnvironmentVariable("KOTODAMA_HTTP_URL"),
        Environment.GetEnvironmentVariable("KOTODAMA_HTTP_TOKEN"));

    /// <summary>Transport名とHTTP URLを検証します。stdio Transportは0.18.0で廃止しました。</summary>
    internal static ServerSettings Parse(string? transportText, string? httpUrlText, string? httpTokenText = null)
    {
        if (transportText is not null && transportText.Equals("stdio", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"KOTODAMA_TRANSPORT=stdio was removed in Kotodama 0.18.0. Run the HTTP service and connect MCP clients to {DefaultHttpUrl}{HttpPath}.");
        }

        if (!string.IsNullOrWhiteSpace(transportText) && !transportText.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("KOTODAMA_TRANSPORT must be http or unset.");
        }

        if (!Uri.TryCreate(string.IsNullOrWhiteSpace(httpUrlText) ? DefaultHttpUrl : httpUrlText, UriKind.Absolute, out var httpUrl))
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

        var httpToken = string.IsNullOrWhiteSpace(httpTokenText) ? null : httpTokenText;
        return new(httpUrl, httpToken);
    }
}
