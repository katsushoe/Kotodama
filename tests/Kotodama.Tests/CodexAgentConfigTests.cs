using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class CodexAgentConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"kotodama-agent-{Guid.NewGuid():N}");

    [Fact]
    public void Update_WhenTemplateExists_InstallsAgentAtomically()
    {
        Directory.CreateDirectory(_directory);
        var templatePath = Path.Combine(_directory, "template.toml");
        var destinationPath = Path.Combine(_directory, "agents", CodexAgentConfig.FileName);
        File.WriteAllText(templatePath, "name = \"kotodama-curator\"\n");

        CodexAgentConfig.Update(destinationPath, templatePath);

        File.ReadAllText(destinationPath).Should().Be("name = \"kotodama-curator\"\n");
        File.Exists(destinationPath + ".kotodama.tmp").Should().BeFalse();
    }

    [Fact]
    public void Update_WhenTemplateIsMissing_ThrowsWithoutChangingExistingAgent()
    {
        Directory.CreateDirectory(_directory);
        var destinationPath = Path.Combine(_directory, CodexAgentConfig.FileName);
        File.WriteAllText(destinationPath, "existing");

        var act = () => CodexAgentConfig.Update(destinationPath, Path.Combine(_directory, "missing.toml"));

        act.Should().Throw<FileNotFoundException>();
        File.ReadAllText(destinationPath).Should().Be("existing");
    }

    [Fact]
    public void Remove_ExistingAgent_DeletesOnlyOwnedFile()
    {
        Directory.CreateDirectory(_directory);
        var destinationPath = Path.Combine(_directory, CodexAgentConfig.FileName);
        var otherPath = Path.Combine(_directory, "other.toml");
        File.WriteAllText(destinationPath, "owned");
        File.WriteAllText(otherPath, "other");

        CodexAgentConfig.Remove(destinationPath);

        File.Exists(destinationPath).Should().BeFalse();
        File.Exists(otherPath).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
