using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class ClaudeHookCommandTests
{
    [Fact]
    public async Task RunAsync_UserPromptSubmit_ReturnsSearchContext()
    {
        using var input = Utf8("{\"hook_event_name\":\"UserPromptSubmit\"}");
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
    public async Task RunAsync_UserPromptSubmitWithNaturalRememberRequest_RequiresKotodamaWrite()
    {
        const string prompt = "Windows版を配布するときはMSIを用意する必要があります。覚えておいて。";
        using var input = Utf8(JsonSerializer.Serialize(new { prompt }, RawJson));
        using var output = new StringWriter();

        await ClaudeHookCommand.RunAsync("claude", "user-prompt-submit", input, output);

        using var result = JsonDocument.Parse(output.ToString());
        var context = result.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString();
        context.Should().Contain("explicit request to persist")
            .And.Contain("Do not satisfy the request with built-in memory")
            .And.Contain("remember_knowledge")
            .And.Contain("Kotodamaに記録しました")
            .And.Contain("only after a successful database write")
            .And.Contain("already_stored");
        prompt.Should().NotContain("Kotodama");
    }

    [Theory]
    [InlineData("{\"stop_hook_active\":false}")]
    [InlineData("{\"stop_hook_active\":true}")]
    public async Task RunAsync_ClaudeStop_NeverBlocks(string json)
    {
        using var input = Utf8(json);
        using var output = new StringWriter();

        await ClaudeHookCommand.RunAsync("claude", "stop", input, output);

        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_UserPromptSubmit_AsksToStoreOnlySupportedFacts()
    {
        using var input = Utf8("{\"prompt\":\"1\"}");
        using var output = new StringWriter();

        await ClaudeHookCommand.RunAsync("claude", "user-prompt-submit", input, output);

        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString()
            .Should().Contain("remember_knowledge").And.Contain("skip messages that are only instructions or questions");
    }

    [Fact]
    public async Task RunAsync_CodexStopFirstInvocation_BlocksForKnowledgeReview()
    {
        using var input = Utf8($"{{\"session_id\":\"{Guid.NewGuid():N}\"}}");
        using var output = new StringWriter();

        await ClaudeHookCommand.RunAsync("codex", "stop", input, output);

        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.GetProperty("decision").GetString().Should().Be("block");
        result.RootElement.GetProperty("reason").GetString().Should().Contain("factual statements suitable for structured knowledge");
        result.RootElement.GetProperty("reason").GetString().Should().Contain("dream gradually reduces confidence");
        result.RootElement.GetProperty("reason").GetString().Should().Contain("explicitly asked to remember");
    }

    [Fact]
    public async Task RunAsync_CodexStopWithoutExplicitRemember_RequiresSupportedFactReview()
    {
        using var input = Utf8($"{{\"session_id\":\"{Guid.NewGuid():N}\"}}");
        using var output = new StringWriter();

        await ClaudeHookCommand.RunAsync("codex", "stop", input, output);

        using var result = JsonDocument.Parse(output.ToString());
        var reason = result.RootElement.GetProperty("reason").GetString();
        reason.Should().Contain("even when the user did not explicitly ask")
            .And.Contain("user's factual text or an identified source")
            .And.Contain("assistant-generated text without an identified source")
            .And.Contain("never pass or store the raw transcript")
            .And.Contain("reconfirm it instead of creating a duplicate")
            .And.Contain("Kotodamaに記録しました")
            .And.Contain("only after a successful database write");
    }

    [Theory]
    [InlineData("user-prompt-submit")]
    [InlineData("stop")]
    public async Task HookProcess_WithRawUtf8JapaneseBeforeEscapedQuote_ParsesInput(string eventName)
    {
        // 日本語の直後の \" をCP932で読むと \ が2バイト文字に取り込まれ、JSON文字列が途中で閉じます。
        var json = "{\"session_id\":\"utf8\",\"prompt\":\"ソ\\\"stdio\\\" と表\\\\ボ 覚えて\",\"stop_hook_active\":false}";
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(typeof(KnowledgeStore).Assembly.Location);
        start.ArgumentList.Add("hook");
        start.ArgumentList.Add("claude");
        start.ArgumentList.Add(eventName);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Hook process did not start.");
        await process.StandardInput.BaseStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(json));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        process.ExitCode.Should().Be(0);
        using var result = JsonDocument.Parse(output);
        if (eventName == "stop") result.RootElement.EnumerateObject().Should().BeEmpty();
        else result.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString().Should().Contain("explicit request to persist");
    }

    [Fact]
    public async Task RunAsync_CodexStopTwice_BlocksThenAllowsStop()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var inputJson = $"{{\"session_id\":\"{sessionId}\"}}";
        using var firstInput = Utf8(inputJson);
        using var firstOutput = new StringWriter();
        using var secondInput = Utf8(inputJson);
        using var secondOutput = new StringWriter();

        await ClaudeHookCommand.RunAsync("codex", "stop", firstInput, firstOutput);
        await ClaudeHookCommand.RunAsync("codex", "stop", secondInput, secondOutput);

        using var firstResult = JsonDocument.Parse(firstOutput.ToString());
        using var secondResult = JsonDocument.Parse(secondOutput.ToString());
        firstResult.RootElement.GetProperty("decision").GetString().Should().Be("block");
        firstResult.RootElement.GetProperty("reason").GetString().Should().Contain("kotodama-curator");
        secondResult.RootElement.EnumerateObject().Should().BeEmpty();
    }

    private static readonly JsonSerializerOptions RawJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static MemoryStream Utf8(string json) => new(System.Text.Encoding.UTF8.GetBytes(json));
}
