using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Kotodama.Tests;

internal static class TagProtocolChecks
{
    internal static async Task VerifyAsync(McpClient client)
    {
        var tools = await client.ListToolsAsync();
        var schema = tools.Single(x => x.Name == "remember_knowledge").JsonSchema.GetProperty("properties").GetProperty("input");
        schema.GetProperty("properties").TryGetProperty("tags", out _).Should().BeTrue();
        schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Should().NotContain("tags");
        tools.Single(x => x.Name == "query_tagged_claims").ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
        var saved = await CallAsync(client, "remember_knowledge", new()
        {
            ["input"] = new
            {
                statement = "Protocol fact",
                entities = new[] { new { key = "a", canonicalName = "Protocol A" }, new { key = "b", canonicalName = "Protocol B" } },
                relations = new[] { new { subject = "a", @object = "b", relationType = "equals" } },
                tags = new[] { "Protocol", "Shared" },
            }
        });
        saved.GetProperty("ok").GetBoolean().Should().BeTrue();
        var statements = await CallAsync(client, "query_tagged_statements", new() { ["input"] = new { tags = new[] { "protocol", "shared" }, tagMatch = "all" } });
        statements.GetArrayLength().Should().Be(1);
        statements[0].GetProperty("statement").GetProperty("canonicalName").GetString().Should().Be("Protocol fact");
        var claims = await CallAsync(client, "query_tagged_claims", new() { ["input"] = new { tags = new[] { "protocol" } } });
        claims.GetArrayLength().Should().Be(2);
        claims[0].GetProperty("tags")[0].GetProperty("origin").GetString().Should().Be("inherited");
        var tag = await CallAsync(client, "create_tag", new() { ["name"] = "Protocol" });
        var id = tag.GetProperty("id").GetInt64();
        await CallAsync(client, "rename_tag", new() { ["tagId"] = id, ["name"] = "Renamed" });
        await CallAsync(client, "add_tag_alias", new() { ["tagId"] = id, ["alias"] = "Alias" });
        var list = await CallAsync(client, "list_tags", new());
        list.GetArrayLength().Should().Be(2);
        var sharedId = list.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Shared").GetProperty("id").GetInt64();
        await CallAsync(client, "merge_tags", new() { ["sourceTagId"] = id, ["targetTagId"] = sharedId });
        var preview = await CallAsync(client, "set_knowledge_tags", new()
        {
            ["input"] = new
            {
                targetKind = "statement",
                targetIds = new[] { saved.GetProperty("statementId").GetInt64() },
                tagIds = new[] { id },
                remove = true,
            }
        });
        preview.GetProperty("matchedCount").GetInt32().Should().Be(1);
        preview.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        await CallAsync(client, "set_knowledge_tags", new()
        {
            ["input"] = new
            {
                targetKind = "statement",
                targetIds = new[] { saved.GetProperty("statementId").GetInt64() },
                tagIds = new[] { id },
                remove = true,
                dryRun = false,
                expectedCount = 1,
            }
        });
        (await CallAsync(client, "query_tagged_statements", new() { ["input"] = new { tags = new[] { "Alias" } } })).GetArrayLength().Should().Be(0);
        (await CallAsync(client, "query_tagged_claims", new() { ["input"] = new { tags = new[] { "Alias" } } })).GetArrayLength().Should().Be(2);
        var invalid = await client.CallToolAsync("query_tagged_claims", new Dictionary<string, object?> { ["input"] = new { tags = new[] { "Protocol" }, tagMatch = "invalid" } });
        invalid.IsError.Should().BeTrue();
    }

    private static async Task<JsonElement> CallAsync(McpClient client, string name, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        result.IsError.Should().NotBeTrue();
        var text = result.StructuredContent is null
            ? result.Content.OfType<TextContentBlock>().Single().Text
            : JsonSerializer.Serialize(result.StructuredContent);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }
}
