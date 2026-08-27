using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class ClaudeHookConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"kotodama-claude-hooks-{Guid.NewGuid():N}");

    [Fact]
    public void Update_NewSettings_AddsBothHooksAndPreservesOtherSettings()
    {
        var path = CreateSettings("{\"model\":\"sonnet\"}");

        ClaudeHookConfig.Update(path, @"C:\Kotodama\bin\Kotodama.exe");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        document.RootElement.GetProperty("model").GetString().Should().Be("sonnet");
        GetCommand(document, "UserPromptSubmit").Should().Contain("hook claude user-prompt-submit");
        GetCommand(document, "Stop").Should().Contain("hook claude stop");
    }

    [Fact]
    public void Update_Repeated_DoesNotDuplicateHooks()
    {
        var path = CreateSettings("{}");

        ClaudeHookConfig.Update(path, @"C:\Kotodama\bin\Kotodama.exe");
        ClaudeHookConfig.Update(path, @"C:\Kotodama\bin\Kotodama.exe");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        document.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Remove_MixedSettings_RemovesOnlyKotodamaHooks()
    {
        var path = CreateSettings("""
            {
              "hooks": {
                "Stop": [
                  { "hooks": [{ "type": "command", "command": "other-tool save" }] }
                ]
              }
            }
            """);
        ClaudeHookConfig.Update(path, @"C:\Kotodama\bin\Kotodama.exe");

        ClaudeHookConfig.Remove(path);

        File.ReadAllText(path).Should().Contain("other-tool save").And.NotContain("--integration-id kotodama");
    }

    private string CreateSettings(string json)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string GetCommand(JsonDocument document, string eventName) =>
        document.RootElement.GetProperty("hooks").GetProperty(eventName)[0]
            .GetProperty("hooks")[0].GetProperty("command").GetString()!;

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
