using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class CodexConfigTests
{
    [Fact]
    public void Update_AddsKotodamaWithoutChangingOtherServers()
    {
        var path = CreateTemporaryConfig("[mcp_servers.shiori]\nurl = \"http://127.0.0.1:1/mcp\"\n");
        try
        {
            CodexConfig.Update(path, UserIntegration.McpUrl);
            var content = File.ReadAllText(path);
            content.Should().Contain("[mcp_servers.shiori]").And.Contain("[mcp_servers.kotodama]");
            content.Should().Contain($"url = \"{UserIntegration.McpUrl}\"");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Update_ReplacesExistingKotodamaSectionWithoutDuplicates()
    {
        var path = CreateTemporaryConfig("[mcp_servers.kotodama]\ncommand = \"old.exe\"\n\n[mcp_servers.shiori]\nurl = \"http://localhost\"\n");
        try
        {
            CodexConfig.Update(path, UserIntegration.McpUrl);
            var content = File.ReadAllText(path);
            content.Split("[mcp_servers.kotodama]").Should().HaveCount(2);
            content.Should().NotContain("old.exe").And.Contain("[mcp_servers.shiori]");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Remove_DeletesOnlyKotodamaSection()
    {
        const string content = "[mcp_servers.kotodama]\ncommand = \"old.exe\"\n[mcp_servers.kotodama.env]\nVALUE = \"old\"\n\n[mcp_servers.shiori]\nurl = \"http://localhost\"\n";
        CodexConfig.RemoveSection(content).Should().NotContain("kotodama").And.NotContain("VALUE").And.Contain("[mcp_servers.shiori]");
    }

    private static string CreateTemporaryConfig(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kotodama-codex-{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, content);
        return path;
    }
}
