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
    public void BuildCreateTaskArguments_StartsHttpServerAtLogonWithLimitedRights()
    {
        var arguments = UserIntegration.BuildCreateTaskArguments(@"C:\Kotodama\bin\Kotodama.exe");

        arguments.Should().ContainInOrder(
            "/Create", "/TN", UserIntegration.TaskName,
            "/SC", "ONLOGON",
            "/TR", "\"C:\\Kotodama\\bin\\Kotodama.exe\" --http",
            "/RL", "LIMITED", "/F");
    }

    [Fact]
    public void GetCodexHooksPath_UsesUserScopedCodexHooksFile()
    {
        UserIntegration.GetCodexHooksPath().Should().EndWith(Path.Combine(".codex", "hooks.json"));
    }
}
