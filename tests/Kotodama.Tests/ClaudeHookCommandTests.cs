using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class ClaudeHookCommandTests
{
    [Fact]
    public async Task RunAsync_UserPromptSubmit_ReturnsSearchContext()
    {
        using var input = new StringReader("{\"hook_event_name\":\"UserPromptSubmit\"}");
        using var output = new StringWriter();

        var exitCode = await ClaudeHookCommand.RunAsync("claude", "user-prompt-submit", input, output);

        exitCode.Should().Be(0);
        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.GetProperty("hookSpecificOutput").GetProperty("hookEventName").GetString()
            .Should().Be("UserPromptSubmit");
        result.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString()
            .Should().Contain("Search first");
    }

    [Fact]
    public async Task RunAsync_StopFirstInvocation_BlocksForKnowledgeReview()
    {
        using var input = new StringReader("{\"stop_hook_active\":false}");
        using var output = new StringWriter();

        await ClaudeHookCommand.RunAsync("claude", "stop", input, output);

        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.GetProperty("decision").GetString().Should().Be("block");
        result.RootElement.GetProperty("reason").GetString().Should().Contain("durable, reusable facts");
    }

    [Fact]
    public async Task RunAsync_StopContinuation_AllowsStopToPreventLoop()
    {
        using var input = new StringReader("{\"stop_hook_active\":true}");
        using var output = new StringWriter();

        await ClaudeHookCommand.RunAsync("claude", "stop", input, output);

        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_CodexStopTwice_BlocksThenAllowsStop()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var inputJson = $"{{\"session_id\":\"{sessionId}\"}}";
        using var firstInput = new StringReader(inputJson);
        using var firstOutput = new StringWriter();
        using var secondInput = new StringReader(inputJson);
        using var secondOutput = new StringWriter();

        await ClaudeHookCommand.RunAsync("codex", "stop", firstInput, firstOutput);
        await ClaudeHookCommand.RunAsync("codex", "stop", secondInput, secondOutput);

        using var firstResult = JsonDocument.Parse(firstOutput.ToString());
        using var secondResult = JsonDocument.Parse(secondOutput.ToString());
        firstResult.RootElement.GetProperty("decision").GetString().Should().Be("block");
        firstResult.RootElement.GetProperty("reason").GetString().Should().Contain("kotodama-curator");
        secondResult.RootElement.EnumerateObject().Should().BeEmpty();
    }
}
