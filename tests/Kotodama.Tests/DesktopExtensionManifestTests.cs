using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class DesktopExtensionManifestTests
{
    private static readonly string ManifestPath = Path.Combine(AppContext.BaseDirectory, "desktop-extension", "manifest.json");

    [Fact]
    public void Manifest_IsValidWindowsBinaryExtension()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var root = document.RootElement;

        root.GetProperty("dxt_version").GetString().Should().Be("0.1");
        root.GetProperty("name").GetString().Should().Be("kotodama");
        root.GetProperty("server").GetProperty("type").GetString().Should().Be("binary");
        root.GetProperty("server").GetProperty("entry_point").GetString().Should().Be("server/Kotodama.exe");
        root.GetProperty("compatibility").GetProperty("platforms").EnumerateArray()
            .Select(value => value.GetString()).Should().ContainSingle().Which.Should().Be("win32");
        root.GetProperty("version").GetString().Should().Be(
            typeof(KotodamaApplication).Assembly.GetName().Version?.ToString(3));
    }

    [Fact]
    public void Manifest_StoresDatabaseOutsideExtensionDirectory()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var environment = document.RootElement.GetProperty("server").GetProperty("mcp_config").GetProperty("env");

        environment.GetProperty("KOTODAMA_DB").GetString().Should().StartWith("${user_config.data_directory}");
        environment.GetProperty("KOTODAMA_LOG_DIR").GetString().Should().StartWith("${user_config.data_directory}");
    }

    [Fact]
    public void Manifest_DeclaresRuntimeDiscoveredToolsAndPrompts()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var root = document.RootElement;

        root.GetProperty("tools_generated").GetBoolean().Should().BeTrue();
        root.GetProperty("prompts_generated").GetBoolean().Should().BeTrue();
    }
}
