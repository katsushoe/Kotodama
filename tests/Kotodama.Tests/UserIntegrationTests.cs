using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class UserIntegrationTests
{
    [Fact]
    public void McpUrl_UsesFixedLoopbackStreamableHttpEndpoint()
    {
        UserIntegration.McpUrl.Should().Be("http://127.0.0.1:39280/mcp");
    }

    [Fact]
    public void LegacyTaskName_MatchesTaskRegisteredByPreviousVersions()
    {
        UserIntegration.LegacyTaskName.Should().Be("Kotodama MCP Server");
    }

    [Fact]
    public void GetCodexHooksPath_UsesUserScopedCodexHooksFile()
    {
        UserIntegration.GetCodexHooksPath().Should().EndWith(Path.Combine(".codex", "hooks.json"));
    }

    [Fact]
    public void GetCodexAgentPath_UsesUserScopedCodexAgentsDirectory()
    {
        UserIntegration.GetCodexAgentPath().Should().EndWith(
            Path.Combine(".codex", "agents", "kotodama-curator.toml"));
    }

    [Fact]
    public void GetCodexAgentTemplatePath_UsesDeploymentRelativeTemplate()
    {
        UserIntegration.GetCodexAgentTemplatePath(@"C:\Kotodama\bin").Should().Be(
            Path.Combine(@"C:\Kotodama\bin", "codex", "kotodama-curator.toml"));
    }
}
