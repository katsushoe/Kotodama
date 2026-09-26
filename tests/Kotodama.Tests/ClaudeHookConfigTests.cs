using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class ClaudeHookConfigTests : IDisposable
{
    private const string ExecutablePath = @"C:\Kotodama\bin\Kotodama.exe";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"kotodama-claude-hooks-{Guid.NewGuid():N}");

    [Fact]
    public void Update_NewSettings_AddsOnlyUserPromptSubmitHookAndPreservesOtherSettings()
    {
        var path = CreateSettings("{\"model\":\"sonnet\"}");

        ClaudeHookConfig.Update(path, ExecutablePath);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        document.RootElement.GetProperty("model").GetString().Should().Be("sonnet");
        GetCommand(document, "UserPromptSubmit").Should().Contain("hook claude user-prompt-submit");
        document.RootElement.GetProperty("hooks").TryGetProperty("Stop", out _).Should().BeFalse();
    }

    [Fact]
    public void Update_Repeated_DoesNotDuplicateHooks()
    {
        var path = CreateSettings("{}");

        ClaudeHookConfig.Update(path, ExecutablePath);
        ClaudeHookConfig.Update(path, ExecutablePath);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        document.RootElement.GetProperty("hooks").GetProperty("UserPromptSubmit").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Update_LegacyStopHook_RemovesOnlyKotodamaStopHook()
    {
        var legacyCommand = ClaudeHookConfig.BuildCommand(ExecutablePath, "stop");
        var path = CreateSettings(JsonSerializer.Serialize(new
        {
            hooks = new
            {
                Stop = new object[]
                {
                    new { hooks = new[] { new { type = "command", command = "other-tool save" } } },
                    new { hooks = new[] { new { type = "command", command = legacyCommand } } },
                },
            },
        }));

        ClaudeHookConfig.Update(path, ExecutablePath);

        var text = File.ReadAllText(path);
        text.Should().Contain("other-tool save").And.NotContain("hook claude stop");
        using var document = JsonDocument.Parse(text);
        document.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Update_WhenUserRemovedStopHook_DoesNotRegisterItAgain()
    {
        var path = CreateSettings("{\"hooks\":{}}");

        ClaudeHookConfig.Update(path, ExecutablePath);
        ClaudeHookConfig.Update(path, ExecutablePath);

        File.ReadAllText(path).Should().NotContain("hook claude stop");
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
        ClaudeHookConfig.Update(path, ExecutablePath);

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
