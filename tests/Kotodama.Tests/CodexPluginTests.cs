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
            .And.Contain("explicitly asks to remember")
            .And.Contain("built-in memory")
            .And.Contain("Do not store raw transcripts")
            .And.NotContain("[TODO:");
    }

    [Fact]
    public void CuratorAgent_RequiresImplicitReviewAndRejectsUnsupportedAssistantText()
    {
        var agent = File.ReadAllText(Path.Combine(PluginDirectory, "assets", "kotodama-curator.toml"));

        agent.Should().Contain("even when the user did not explicitly ask")
            .And.Contain("user's factual text or an identified source")
            .And.Contain("assistant-generated text without an identified source")
            .And.Contain("pass only that factual statement without rewriting it")
            .And.Contain("Do not store raw conversation transcripts");
    }
}
