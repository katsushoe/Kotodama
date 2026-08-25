using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kotodama.Tests;

public sealed class McpStdioTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"kotodama-mcp-{Guid.NewGuid():N}.db");
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["KOTODAMA_DB"] = _databasePath;
        environment["KOTODAMA_DREAM_TEMP_STORE"] = "Memory";
        var serverAssembly = typeof(KnowledgeStore).Assembly.Location;
        var transport = new StdioClientTransport(new()
        {
            Name = "Kotodama integration test",
            Command = "dotnet",
            Arguments = [serverAssembly],
            WorkingDirectory = Path.GetDirectoryName(serverAssembly),
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            ShutdownTimeout = TimeSpan.FromMilliseconds(500),
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await DeleteDatabaseFilesAsync();
    }

    [Fact]
    public void Stdio_WhenInitialized_ExposesServerIdentityAndCapabilities()
    {
        _client.ServerInfo.Name.Should().NotBeNullOrWhiteSpace();
        _client.ServerCapabilities.Tools.Should().NotBeNull();
        _client.ServerCapabilities.Prompts.Should().NotBeNull();
        _client.ServerInstructions.Should().Contain("persistent structured knowledge");
        _client.ServerInstructions.Should().Contain("Do not store secrets");
    }

    [Fact]
    public async Task ListPrompts_ReturnsKotodamaGluePrompt()
    {
        var prompts = await _client.ListPromptsAsync(cancellationToken: CancellationToken.None);

        prompts.Should().ContainSingle(x => x.Name == "use_kotodama");
    }

    [Fact]
    public async Task GetPrompt_ReturnsKnowledgeRegistrationWorkflow()
    {
        var result = await _client.GetPromptAsync("use_kotodama", cancellationToken: CancellationToken.None);
        var text = result.Messages.Select(x => x.Content).OfType<TextContentBlock>().Single().Text;

        text.Should().Contain("search_entities");
        text.Should().Contain("propose_claim");
        text.Should().Contain("ask the user");
    }

    [Fact]
    public async Task ListTools_ReturnsAllExpectedTools()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: CancellationToken.None);

        tools.Select(x => x.Name).Should().BeEquivalentTo(
            "get_version", "get_entity", "search_entities", "propose_claim", "retract_claim",
            "query_claims", "query_relations", "get_neighbors", "get_knowledge_context",
            "run_dream", "create_entity", "create_relation_type", "create_event");
    }

    [Fact]
    public async Task GetVersion_ThroughStdio_ReturnsServerIdentity()
    {
        var result = await _client.CallToolAsync("get_version", cancellationToken: CancellationToken.None);

        result.IsError.Should().NotBeTrue();
        GetResponseJson(result).Should().Contain("Kotodama").And.Contain("0.3.1");
    }

    [Fact]
    public async Task ClaimWorkflow_ThroughStdio_PersistsAndQueriesClaim()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = await CreateEntityAsync("Subject" + suffix);
        var objectId = await CreateEntityAsync("Object" + suffix);
        await CreateRelationTypeAsync("relation_" + suffix, "Permanent", null);

        var proposed = await CallAsync("propose_claim", new Dictionary<string, object?>
        {
            ["candidate"] = new { subjectId, objectId, relationType = "relation_" + suffix, confidence = 0.9 },
        });
        var queried = await CallAsync("query_claims", new Dictionary<string, object?> { ["entityId"] = subjectId });

        GetResponseJson(proposed).Should().Contain("accepted");
        GetResponseJson(queried).Should().Contain("relation_" + suffix);
    }

    [Fact]
    public async Task RunDream_ThroughStdio_MarksExpiredClaimStale()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = await CreateEntityAsync("DreamSubject" + suffix);
        var objectId = await CreateEntityAsync("DreamObject" + suffix);
        await CreateRelationTypeAsync("dream_" + suffix, "Periodic", 1);
        await CallAsync("propose_claim", new Dictionary<string, object?>
        {
            ["candidate"] = new { subjectId, objectId, relationType = "dream_" + suffix, observedAt = "2020-01-01T00:00:00Z" },
        });

        var result = await CallAsync("run_dream");

        GetResponseJson(result).Should().Contain("markedStale").And.Contain("1");
    }

    [Fact]
    public async Task InvalidCandidate_ThroughStdio_ReturnsBusinessRejection()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = await CreateEntityAsync("InvalidSubject" + suffix);
        var objectId = await CreateEntityAsync("InvalidObject" + suffix);
        await CreateRelationTypeAsync("invalid_" + suffix, "Permanent", null);

        var result = await CallAsync("propose_claim", new Dictionary<string, object?>
        {
            ["candidate"] = new { subjectId, objectId, relationType = "invalid_" + suffix, confidence = 2.0 },
        });

        result.IsError.Should().NotBeTrue();
        GetResponseJson(result).Should().Contain("rejected").And.Contain("confidence");
    }

    private async Task<long> CreateEntityAsync(string name)
    {
        var result = await CallAsync("create_entity", new Dictionary<string, object?> { ["input"] = new { canonicalName = name } });
        using var json = JsonDocument.Parse(GetResponseJson(result));
        return json.RootElement.GetProperty("id").GetInt64();
    }

    private async Task CreateRelationTypeAsync(string name, string freshnessPolicy, long? refreshAfterSeconds) =>
        await CallAsync("create_relation_type", new Dictionary<string, object?>
        {
            ["input"] = new { canonicalName = name, category = "state", kind = "Directed", freshnessPolicy, refreshAfterSeconds },
        });

    private async Task<ModelContextProtocol.Protocol.CallToolResult> CallAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null) =>
        await _client.CallToolAsync(name, arguments, cancellationToken: CancellationToken.None);

    private static string GetResponseJson(CallToolResult result)
    {
        if (result.StructuredContent is not null) return JsonSerializer.Serialize(result.StructuredContent);
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }

    private async Task DeleteDatabaseFilesAsync()
    {
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            for (var attempt = 0; attempt < 20 && File.Exists(path); attempt++)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException) when (attempt < 19)
                {
                    await Task.Delay(100);
                }
            }
        }
    }
}
