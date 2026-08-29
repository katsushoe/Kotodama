using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class ServerSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("stdio")]
    [InlineData("STDIO")]
    public void Parse_WhenStdioOrEmpty_ReturnsStdio(string? value)
    {
        var settings = ServerSettings.Parse(value, null);

        settings.Should().Be(new ServerSettings(McpTransport.Stdio, null, null));
    }

    [Fact]
    public void Parse_WhenHttpLoopback_ReturnsHttp()
    {
        var settings = ServerSettings.Parse("http", "http://127.0.0.1:3456");

        settings.Should().Be(new ServerSettings(McpTransport.Http, new Uri("http://127.0.0.1:3456"), null));
    }

    [Fact]
    public void Parse_WhenHttpTokenIsConfigured_PreservesToken()
    {
        var settings = ServerSettings.Parse("http", "http://127.0.0.1:3456", "secret-token");

        settings.HttpToken.Should().Be("secret-token");
    }

    [Theory]
    [InlineData("tcp", "http://127.0.0.1:3456", "KOTODAMA_TRANSPORT")]
    [InlineData("http", null, "KOTODAMA_HTTP_URL")]
    [InlineData("http", "ftp://127.0.0.1:3456", "HTTP or HTTPS")]
    [InlineData("http", "http://192.0.2.1:3456", "loopback")]
    [InlineData("http", "http://127.0.0.1:3456/path", "scheme, loopback host, and port")]
    public void Parse_WhenInvalid_Throws(string transport, string? url, string expected)
    {
        var action = () => ServerSettings.Parse(transport, url);

        action.Should().Throw<InvalidOperationException>().WithMessage($"*{expected}*");
    }
}
