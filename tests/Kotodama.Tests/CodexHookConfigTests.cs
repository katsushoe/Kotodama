using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class CodexHookConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"kotodama-codex-hooks-{Guid.NewGuid():N}");

    [Fact]
    public void Update_ExistingHooks_AppendsKotodamaHooksAndPreservesExistingHooks()
    {
        var path = CreateSettings("""
            {
              "hooks": {
                "Stop": [{ "hooks": [{ "type": "command", "command": "other-tool stop" }] }]
              }
            }
            """);

        CodexHookConfig.Update(path, @"C:\Kotodama\bin\Kotodama.exe");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var stopHooks = document.RootElement.GetProperty("hooks").GetProperty("Stop");
        stopHooks.GetArrayLength().Should().Be(2);
        stopHooks[0].GetProperty("hooks")[0].GetProperty("command").GetString().Should().Be("other-tool stop");
        stopHooks[1].GetProperty("hooks")[0].GetProperty("commandWindows").GetString()
            .Should().Contain("hook codex stop --integration-id kotodama");
    }

    [Fact]
    public void Update_Repeated_DoesNotDuplicateKotodamaHooks()
    {
        var path = CreateSettings("{}");

        CodexHookConfig.Update(path, @"C:\Kotodama\bin\Kotodama.exe");
        CodexHookConfig.Update(path, @"C:\Kotodama\bin\Kotodama.exe");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        document.RootElement.GetProperty("hooks").GetProperty("UserPromptSubmit").GetArrayLength().Should().Be(1);
        document.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Remove_MixedHooks_RemovesOnlyKotodamaHooks()
    {
        var path = CreateSettings("""
            {
              "description": "existing",
              "hooks": {
                "Stop": [{ "hooks": [{ "type": "command", "command": "other-tool stop" }] }]
              }
            }
            """);
        CodexHookConfig.Update(path, @"C:\Kotodama\bin\Kotodama.exe");

        CodexHookConfig.Remove(path);

        File.ReadAllText(path).Should().Contain("other-tool stop").And.NotContain("--integration-id kotodama");
    }

    private string CreateSettings(string json)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "hooks.json");
        File.WriteAllText(path, json);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
