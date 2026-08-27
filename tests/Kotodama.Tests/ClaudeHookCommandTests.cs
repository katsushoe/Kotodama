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

        var exitCode = await ClaudeHookCommand.RunAsync("user-prompt-submit", input, output);

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

        await ClaudeHookCommand.RunAsync("stop", input, output);

        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.GetProperty("decision").GetString().Should().Be("block");
        result.RootElement.GetProperty("reason").GetString().Should().Contain("durable, reusable facts");
    }

    [Fact]
    public async Task RunAsync_StopContinuation_AllowsStopToPreventLoop()
    {
        using var input = new StringReader("{\"stop_hook_active\":true}");
        using var output = new StringWriter();

        await ClaudeHookCommand.RunAsync("stop", input, output);

        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.EnumerateObject().Should().BeEmpty();
    }
}
