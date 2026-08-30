using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class CodexPluginTests
{
    private static readonly string PluginDirectory = Path.Combine(AppContext.BaseDirectory, "codex-plugin");

    [Fact]
    public void Manifest_IdentifiesVersionedKotodamaPlugin()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(PluginDirectory, "plugin.json")));
        var root = document.RootElement;

        root.GetProperty("name").GetString().Should().Be("kotodama");
        root.GetProperty("version").GetString().Should().Be(
            typeof(KotodamaApplication).Assembly.GetName().Version?.ToString(3));
        root.GetProperty("skills").GetString().Should().Be("./skills/");
        root.GetProperty("mcpServers").GetString().Should().Be("./.mcp.json");
    }

    [Fact]
    public void McpConfig_UsesKotodamaLoopbackEndpoint()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(PluginDirectory, ".mcp.json")));

        var server = document.RootElement.GetProperty("mcpServers").GetProperty("kotodama");
        server.GetProperty("type").GetString().Should().Be("http");
        server.GetProperty("url").GetString().Should().Be(UserIntegration.McpUrl);
    }

    [Fact]
    public void Skill_DefinesDurableKnowledgeAndCuratorBoundaries()
    {
        var skill = File.ReadAllText(Path.Combine(PluginDirectory, "skills", "kotodama-knowledge", "SKILL.md"));

        skill.Should().Contain("name: kotodama-knowledge")
            .And.Contain("kotodama-curator")
            .And.Contain("Do not store raw transcripts")
            .And.NotContain("[TODO:");
    }
}
