using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kotodama.Tests;

public sealed class McpHttpTests : IAsyncLifetime
{
    private const string HttpToken = "integration-test-token";
    private KotodamaHttpTestServer _server = null!;
    private Uri _endpoint = null!;
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        _server = KotodamaHttpTestServer.Start(HttpToken);
        _endpoint = _server.Endpoint;
        _client = await _server.ConnectAsync(HttpToken);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        if (_server is not null) await _server.DisposeAsync();
    }

    [Fact]
    public void Initialize_ThroughHttp_ReturnsInstructionsAndCapabilities()
    {
        _client.ServerCapabilities.Tools.Should().NotBeNull();
        _client.ServerCapabilities.Prompts.Should().NotBeNull();
        _client.ServerInstructions.Should().Contain("persistent structured knowledge");
        _client.ServerInstructions.Should().Contain("explicitly asks to remember");
        _client.ServerInstructions.Should().Contain("even if the user did not explicitly ask");
        _client.ServerInstructions.Should().Contain("Kotodamaに記録しました")
            .And.Contain("only after a successful database write")
            .And.Contain("already_stored");
    }

    [Fact]
    public Task Tags_ThroughHttp_PreserveSchemaStateAndErrors() => TagProtocolChecks.VerifyAsync(_client);

    [Fact]
    public async Task CallCommand_ThroughHttp_UsesSameToolsAndErrorContract()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kotodama-cli-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, "{\"name\":\"CLI tag\"}");
            var created = await RunToolCommandAsync("create_tag", path);
            created.ExitCode.Should().Be(0);
            created.Output.Should().Contain("CLI tag");
            var tags = await _client.CallToolAsync("list_tags");
            GetResponseJson(tags).Should().Contain("CLI tag");
            await File.WriteAllTextAsync(path, "{\"input\":{\"tags\":[\"CLI tag\"],\"tagMatch\":\"invalid\"}}");
            (await RunToolCommandAsync("query_tagged_claims", path)).ExitCode.Should().Be(1);
            await File.WriteAllTextAsync(path, "[]");
            (await RunToolCommandAsync("list_tags", path)).ExitCode.Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private async Task<(int ExitCode, string Output)> RunToolCommandAsync(string tool, string path)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(KnowledgeStore).Assembly.Location);
        start.ArgumentList.Add("call");
        start.ArgumentList.Add(tool);
        start.ArgumentList.Add(path);
        start.Environment["KOTODAMA_HTTP_URL"] = _endpoint.GetLeftPart(UriPartial.Authority);
        start.Environment["KOTODAMA_HTTP_TOKEN"] = HttpToken;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("CLI did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, output);
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
        }
    }

    [Fact]
    public async Task GetVersion_ThroughHttp_ReturnsServerIdentity()
    {
        var result = await _client.CallToolAsync("get_version", cancellationToken: CancellationToken.None);

        GetResponseJson(result).Should().Contain("Kotodama").And.Contain("0.18.1").And.Contain("protocolVersion");
    }

    [Fact]
    public async Task CreateEntity_ThroughHttp_PersistsEntity()
    {
        var name = "HttpEntity" + Guid.NewGuid().ToString("N");
        await _client.CallToolAsync("create_entity", new Dictionary<string, object?> { ["input"] = new { canonicalName = name } });
        var result = await _client.CallToolAsync("search_entities", new Dictionary<string, object?> { ["query"] = name });

        GetResponseJson(result).Should().Contain(name);
    }

    [Fact]
    public async Task ProposeClaim_ThroughHttp_WhenReferencesAreMissing_ReturnsBusinessError()
    {
        var result = await _client.CallToolAsync("propose_claim", new Dictionary<string, object?>
        {
            ["candidate"] = new { subjectId = long.MaxValue, objectId = long.MaxValue - 1, relationType = "missing" },
        });

        result.IsError.Should().NotBeTrue();
        GetResponseJson(result).Should().Contain("rejected");
    }

    [Fact]
    public async Task RememberGraph_ThroughHttp_StoresStructureAndRejectsNegativeEquality()
    {
        var input = new StructuredKnowledgeInput("HTTP graph " + Guid.NewGuid().ToString("N"),
            [new("a", "HTTP A"), new("b", "HTTP B")], [new("a", "b", "equals")]);
        var stored = await _client.CallToolAsync("remember_knowledge", new Dictionary<string, object?> { ["input"] = input });
        stored.IsError.Should().NotBeTrue();
        using var json = JsonDocument.Parse(GetResponseJson(stored));
        json.RootElement.GetProperty("structureStatus").GetString().Should().Be("structured");
        var invalid = input with { Relations = [new("a", "b", "canonical_of", Polarity.Negative)] };
        var rejected = await _client.CallToolAsync("remember_knowledge", new Dictionary<string, object?> { ["input"] = invalid });
        GetResponseJson(rejected).Should().Contain("rejected").And.Contain("Negative");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-token")]
    public async Task McpEndpoint_WhenBearerTokenIsInvalid_ReturnsUnauthorized(string? authorization)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { } }),
        };
        if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle(value => value.Scheme == "Bearer");
    }

    [Fact]
    public async Task McpEndpoint_WhenBearerTokenIsNotConfigured_AllowsLoopbackClient()
    {
        await using var server = KotodamaHttpTestServer.Start(null);
        await using var client = await server.ConnectAsync(null);

        client.ServerCapabilities.Tools.Should().NotBeNull();
    }

    [Fact]
    public async Task Server_WhenStdioTransportIsRequested_ExitsWithError()
    {
        await using var server = KotodamaHttpTestServer.Start(null, new Dictionary<string, string> { ["KOTODAMA_TRANSPORT"] = "stdio" });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var exitCode = await server.WaitForExitAsync(timeout.Token);

        exitCode.Should().NotBe(0);
    }

    private static string GetResponseJson(CallToolResult result)
    {
        if (result.StructuredContent is not null) return JsonSerializer.Serialize(result.StructuredContent);
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }
}
