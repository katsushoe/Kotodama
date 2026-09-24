using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class ServerSettingsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("http", null)]
    [InlineData("HTTP", " ")]
    public void Parse_WhenTransportOrUrlIsOmitted_UsesDefaultHttpUrl(string? transport, string? url)
    {
        var settings = ServerSettings.Parse(transport, url);

        settings.Should().Be(new ServerSettings(new Uri(ServerSettings.DefaultHttpUrl), null));
    }

    [Fact]
    public void Parse_WhenHttpLoopback_ReturnsHttp()
    {
        var settings = ServerSettings.Parse("http", "http://127.0.0.1:3456");

        settings.Should().Be(new ServerSettings(new Uri("http://127.0.0.1:3456"), null));
    }

    [Fact]
    public void Parse_WhenHttpTokenIsConfigured_PreservesToken()
    {
        var settings = ServerSettings.Parse("http", "http://127.0.0.1:3456", "secret-token");

        settings.HttpToken.Should().Be("secret-token");
    }

    [Theory]
    [InlineData("stdio")]
    [InlineData("STDIO")]
    public void Parse_WhenStdioIsRequested_ThrowsRemovalGuidance(string transport)
    {
        var action = () => ServerSettings.Parse(transport, null);

        action.Should().Throw<InvalidOperationException>().WithMessage("*removed in Kotodama 0.18.0*http://127.0.0.1:39280/mcp*");
    }

    [Theory]
    [InlineData("tcp", "http://127.0.0.1:3456", "KOTODAMA_TRANSPORT")]
    [InlineData("http", "not a url", "KOTODAMA_HTTP_URL")]
    [InlineData("http", "ftp://127.0.0.1:3456", "HTTP or HTTPS")]
    [InlineData("http", "http://192.0.2.1:3456", "loopback")]
    [InlineData("http", "http://127.0.0.1:3456/path", "scheme, loopback host, and port")]
    public void Parse_WhenInvalid_Throws(string transport, string? url, string expected)
    {
        var action = () => ServerSettings.Parse(transport, url);

        action.Should().Throw<InvalidOperationException>().WithMessage($"*{expected}*");
    }
}
