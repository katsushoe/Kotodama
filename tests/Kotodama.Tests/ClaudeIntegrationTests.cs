using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class ClaudeIntegrationTests
{
    [Fact]
    public void BuildAddArguments_RegistersHttpServerInUserScope()
    {
        ClaudeIntegration.BuildAddArguments().Should().ContainInOrder(
            "mcp", "add", "--transport", "http", "--scope", "user", "kotodama", "http://127.0.0.1:39280/mcp");
    }

    [Fact]
    public void BuildRemoveArguments_RemovesUserScopeServer()
    {
        ClaudeIntegration.BuildRemoveArguments().Should().ContainInOrder(
            "mcp", "remove", "--scope", "user", "kotodama");
    }

    [Fact]
    public void BuildHookCommand_UsesQuotedExecutableAndIntegrationMarker()
    {
        ClaudeHookConfig.BuildCommand(@"C:\Program Files\Kotodama\Kotodama.exe", "stop")
            .Should().Be("\"C:\\Program Files\\Kotodama\\Kotodama.exe\" hook claude stop --integration-id kotodama");
    }
}
