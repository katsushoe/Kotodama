using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kotodama.Tests;

public sealed class McpToolWorkflowTests : IAsyncLifetime
{
    private KotodamaHttpTestServer _server = null!;
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        _server = KotodamaHttpTestServer.Start(null);
        _client = await _server.ConnectAsync(null);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        if (_server is not null) await _server.DisposeAsync();
    }

    [Fact]
    public void Initialize_ExposesServerIdentityAndCapabilities()
    {
        _client.ServerInfo.Name.Should().NotBeNullOrWhiteSpace();
        _client.ServerCapabilities.Tools.Should().NotBeNull();
        _client.ServerCapabilities.Prompts.Should().NotBeNull();
        _client.ServerInstructions.Should().Contain("persistent structured knowledge");
        _client.ServerInstructions.Should().Contain("Do not store secrets");
        _client.ServerInstructions.Should().Contain("explicitly asks to remember");
        _client.ServerInstructions.Should().Contain("Do not substitute built-in memory");
        _client.ServerInstructions.Should().Contain("call remember_knowledge");
        _client.ServerInstructions.Should().Contain("even if the user did not explicitly ask");
        _client.ServerInstructions.Should().Contain("Do not merely acknowledge or summarize");
        _client.ServerInstructions.Should().Contain("Kotodamaに記録しました")
            .And.Contain("only after a successful database write")
            .And.Contain("already_stored");
    }

    [Fact]
    public Task Tags_ThroughHttp_PreserveSchemaStateAndErrors() => TagProtocolChecks.VerifyAsync(_client);

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
        text.Should().Contain("call remember_knowledge during the current turn");
        text.Should().Contain("even when the user did not explicitly ask");
        text.Should().Contain("Kotodamaに記録しました")
            .And.Contain("never for already_stored");
    }

    [Fact]
    public async Task ListTools_ReturnsAllExpectedTools()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: CancellationToken.None);

        tools.Select(x => x.Name).Should().BeEquivalentTo(
            "get_version", "get_entity", "get_knowledge_input", "get_statement", "search_entities", "propose_claim", "retract_claim", "reactivate_claim", "delete_claim",
            "query_claims", "query_relations", "get_neighbors", "get_knowledge_context",
            "create_tag", "list_tags", "rename_tag", "add_tag_alias", "merge_tags", "set_knowledge_tags", "query_tagged_inputs", "query_tagged_statements", "query_tagged_claims",
            "run_dream", "create_entity", "create_relation_type", "update_relation_type", "delete_relation_type", "create_event", "remember_knowledge", "query_events", "get_equivalent_entities", "merge_similarity_groups");
    }

    [Fact]
    public async Task GetVersion_ThroughHttp_ReturnsServerIdentity()
    {
        var result = await _client.CallToolAsync("get_version", cancellationToken: CancellationToken.None);

        result.IsError.Should().NotBeTrue();
        GetResponseJson(result).Should().Contain("Kotodama").And.Contain("0.19.0").And.Contain("protocolVersion");
    }

    [Fact]
    public async Task RememberSchema_RequiresStatementAndBothArrays()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: CancellationToken.None);
        var schema = tools.Single(x => x.Name == "remember_knowledge").JsonSchema;
        var required = schema.GetProperty("properties").GetProperty("input").GetProperty("required");
        required.EnumerateArray().Select(x => x.GetString()).Should().Contain(["statement", "entities", "relations"]);
        var missing = await CallAsync("remember_knowledge", new Dictionary<string, object?> { ["input"] = new { statement = "Missing arrays" } });
        missing.IsError.Should().BeTrue();
        var search = await CallAsync("search_entities", new Dictionary<string, object?> { ["query"] = "Missing arrays" });
        using var result = JsonDocument.Parse(GetResponseJson(search));
        result.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task RememberGraph_ThroughHttp_ReturnsPersistedSourceReference()
    {
        var input = new StructuredKnowledgeInput("MCP graph " + Guid.NewGuid().ToString("N"),
            [new("a", "MCP A"), new("b", "MCP B")], [new("a", "b", "similar_to", Strength: .8)]);
        var result = await CallAsync("remember_knowledge", new Dictionary<string, object?> { ["input"] = input });
        result.IsError.Should().NotBeTrue();
        using var stored = JsonDocument.Parse(GetResponseJson(result));
        stored.RootElement.GetProperty("structureStatus").GetString().Should().Be("structured");
        var inputId = stored.RootElement.GetProperty("inputId").GetInt64();
        var claims = await CallAsync("query_claims", new Dictionary<string, object?> { ["relationType"] = "similar_to" });
        using var queried = JsonDocument.Parse(GetResponseJson(claims));
        queried.RootElement.EnumerateArray().Should().Contain(x => x.GetProperty("sourceInputId").GetInt64() == inputId);
    }

    [Fact]
    public async Task ClaimWorkflow_ThroughHttp_PersistsAndQueriesClaim()
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
    public async Task RememberKnowledge_ThroughHttp_PersistsOnlyUnorderedTerms()
    {
        var text = "自然文のバックアップ予定 " + Guid.NewGuid().ToString("N");

        var remembered = await CallAsync("remember_knowledge", new Dictionary<string, object?>
        {
            ["input"] = new { statement = text, entities = Array.Empty<object>(), relations = Array.Empty<object>(), reason = "Term-only input" },
        });
        using var rememberedDocument = JsonDocument.Parse(GetResponseJson(remembered));
        var inputId = rememberedDocument.RootElement.GetProperty("inputId").GetInt64();
        var input = await CallAsync("get_knowledge_input", new Dictionary<string, object?>
        {
            ["id"] = inputId,
        });
        var searched = await CallAsync("search_entities", new Dictionary<string, object?> { ["query"] = text });

        GetResponseJson(remembered).Should().Contain("stored");
        using var inputDocument = JsonDocument.Parse(GetResponseJson(input));
        inputDocument.RootElement.TryGetProperty("text", out _).Should().BeFalse();
        inputDocument.RootElement.GetProperty("terms").GetArrayLength().Should().BeGreaterThan(0);
        using var searchDocument = JsonDocument.Parse(GetResponseJson(searched));
        searchDocument.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task RememberKnowledge_ThroughHttp_PersistsAndQueriesStructuredEvent()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var actor = "部長" + suffix;
        var remembered = await CallAsync("remember_knowledge", new Dictionary<string, object?>
        {
            ["input"] = new
            {
                statement = "今週、部長が福岡に来る " + suffix,
                entities = Array.Empty<object>(),
                relations = Array.Empty<object>(),
                reason = "Event structure is supplied separately",
                @event = new
                {
                    actor,
                    action = "visit",
                    place = "福岡",
                    startsAt = "2026-09-01T00:00:00+09:00",
                    endsAt = "2026-09-07T00:00:00+09:00",
                },
            },
        });
        var queried = await CallAsync("query_events", new Dictionary<string, object?>
        {
            ["actor"] = actor,
            ["from"] = "2026-09-03T00:00:00+09:00",
            ["to"] = "2026-09-04T00:00:00+09:00",
        });

        GetResponseJson(remembered).Should().Contain("eventId");
        using var document = JsonDocument.Parse(GetResponseJson(queried));
        var eventRecord = document.RootElement.EnumerateArray().Should().ContainSingle().Which;
        eventRecord.GetProperty("actor").GetString().Should().Be(actor);
        eventRecord.GetProperty("place").GetString().Should().Be("福岡");
        eventRecord.GetProperty("action").GetString().Should().Be("visit");
    }

    [Fact]
    public async Task RunDream_ThroughHttp_MarksExpiredClaimStale()
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
    public async Task InvalidCandidate_ThroughHttp_ReturnsBusinessRejection()
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
}
