using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kotodama.Tests;

public sealed class NamespaceMergeTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kotodama-namespace-{Guid.NewGuid():N}.db");
    private KnowledgeStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new(_path, TimeProvider.System);
        await _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _path, _path + "-wal", _path + "-shm" }) File.Delete(path);
        return Task.CompletedTask;
    }

    private Task<RememberKnowledgeResult> RememberAsync(string ns, string text, string a, string b, params string[] tags) =>
        _store.RememberStructuredKnowledgeAsync(new(text, [new("a", a), new("b", b)], [new("a", "b", "similar_to", Strength: .7)],
            Namespace: ns, Tags: tags));

    [Fact]
    public async Task DryRun_ReportsMovesAndMergesWithoutChanges()
    {
        await RememberAsync("Moyai", "Shared and target", "Shared", "TargetOnly");
        await RememberAsync("moyai", "Shared and source", "Shared", "SourceOnly", "source-tag");
        var before = await CountsAsync();

        var result = await _store.MergeNamespaceAsync(new("moyai", "Moyai"));

        result.DryRun.Should().BeTrue();
        result.Applied.Should().BeFalse();
        // 同名・同classの概念"Shared"と、両入力から抽出された同名Termの両方が統合対象です。
        result.EntitiesMerged.Should().Be(2);
        result.MergedEntityIds.Should().HaveCount(2);
        result.EntitiesMoved.Should().BeGreaterThan(0);
        result.InputsMoved.Should().Be(1);
        result.TagsMoved.Should().Be(1);
        result.TotalAffected.Should().Be(result.EntitiesMoved + result.EntitiesMerged + result.InputsMoved + result.TagsMoved);
        result.Conflicts.Should().BeEmpty();
        (await CountsAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Execute_MovesRowsMergesDuplicatesAndKeepsClaimsQueryable()
    {
        var target = await RememberAsync("Moyai", "Shared and target", "Shared", "TargetOnly");
        var source = await RememberAsync("moyai", "Shared and source", "Shared", "SourceOnly", "source-tag");
        var targetShared = target.EntityIds["a"];
        var sourceShared = source.EntityIds["a"];
        var dryRun = await _store.MergeNamespaceAsync(new("moyai", "Moyai"));

        var result = await _store.MergeNamespaceAsync(new("moyai", "Moyai", DryRun: false, ExpectedCount: dryRun.TotalAffected));

        result.Applied.Should().BeTrue();
        result.MergedEntityIds.Should().Contain(sourceShared);
        (await _store.GetEntityAsync(sourceShared)).Should().BeNull();
        var claims = await _store.QueryClaimsAsync(targetShared);
        claims.Should().HaveCount(2, "the source claim now points at the surviving target entity");
        claims.Should().Contain(claim => claim.SourceInputId == source.InputId);
        var input = await _store.GetKnowledgeInputAsync(source.InputId);
        input!.Namespace.Should().Be("Moyai");
        (await _store.ListTagsAsync("Moyai")).Should().ContainSingle(tag => tag.Name == "source-tag");
        (await _store.QueryTaggedInputsAsync(new(Tags: ["source-tag"], Namespace: "Moyai")))
            .Should().ContainSingle().Which.Input.Id.Should().Be(source.InputId);

        await using var connection = new SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM entities WHERE namespace='moyai')
                 + (SELECT COUNT(*) FROM knowledge_inputs WHERE namespace='moyai')
                 + (SELECT COUNT(*) FROM claim_search WHERE subject_namespace='moyai' OR object_namespace='moyai'
                        OR subject_id=$source OR object_id=$source)
            """;
        command.Parameters.AddWithValue("$source", sourceShared);
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(0);
        command.CommandText = "PRAGMA foreign_key_check";
        (await command.ExecuteScalarAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Execute_WithWrongExpectedCount_RejectsWithoutChanges()
    {
        await RememberAsync("moyai", "Only source", "SourceA", "SourceB");
        var before = await CountsAsync();

        var action = () => _store.MergeNamespaceAsync(new("moyai", "Moyai", DryRun: false, ExpectedCount: 999));

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*expectedCount*");
        (await CountsAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Execute_WithTagNameConflict_ReportsConflictAndDoesNotApply()
    {
        await RememberAsync("Moyai", "Target tagged", "TargetA", "TargetB", "shared-tag");
        await RememberAsync("moyai", "Source tagged", "SourceA", "SourceB", "shared-tag");
        var dryRun = await _store.MergeNamespaceAsync(new("moyai", "Moyai"));
        var before = await CountsAsync();

        var result = await _store.MergeNamespaceAsync(new("moyai", "Moyai", DryRun: false, ExpectedCount: dryRun.TotalAffected));

        result.Applied.Should().BeFalse();
        result.Conflicts.Should().ContainSingle().Which.Should().Contain("tag_name_conflict");
        (await CountsAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Execute_WithSymmetricRelationBetweenMergedEntitiesReportsConflict()
    {
        var target = await RememberAsync("Moyai", "Shared target", "Shared", "TargetOnly");
        var source = await RememberAsync("moyai", "Shared source", "Shared", "SourceOnly");
        await using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO relations(relation_type_id,relation_kind,created_at) SELECT id,'symmetric','2026-10-10T00:00:00Z' FROM relation_types WHERE canonical_name='similar_to'";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "SELECT last_insert_rowid()";
            var relationId = Convert.ToInt64(await command.ExecuteScalarAsync());
            command.CommandText = "INSERT INTO symmetric_relations(relation_id,entity_a_id,entity_b_id) VALUES($relation,MIN($source,$target),MAX($source,$target))";
            command.Parameters.AddWithValue("$relation", relationId);
            command.Parameters.AddWithValue("$source", source.EntityIds["a"]);
            command.Parameters.AddWithValue("$target", target.EntityIds["a"]);
            await command.ExecuteNonQueryAsync();
        }
        var before = await CountsAsync();
        var dryRun = await _store.MergeNamespaceAsync(new("moyai", "Moyai"));

        dryRun.Applied.Should().BeFalse();
        dryRun.Conflicts.Should().ContainSingle().Which.Should().Contain("symmetric_self_relation");
        var result = await _store.MergeNamespaceAsync(new("moyai", "Moyai", DryRun: false, ExpectedCount: dryRun.TotalAffected));

        result.Applied.Should().BeFalse();
        result.Conflicts.Should().ContainSingle().Which.Should().Contain("symmetric_self_relation");
        (await CountsAsync()).Should().Be(before);
    }

    [Theory]
    [InlineData("Moyai", "Moyai", "*must differ*")]
    [InlineData("absent", "Moyai", "*has no entities*")]
    public async Task Merge_InvalidNamespaces_Throws(string source, string target, string message)
    {
        var action = () => _store.MergeNamespaceAsync(new(source, target));

        await action.Should().ThrowAsync<ArgumentException>().WithMessage(message);
    }

    private async Task<string> CountsAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT group_concat(namespace || ':' || n, ',') FROM (
                SELECT namespace, COUNT(*) n FROM entities GROUP BY namespace
                UNION ALL SELECT 'inputs-' || namespace, COUNT(*) FROM knowledge_inputs GROUP BY namespace
                UNION ALL SELECT 'tags-' || namespace, COUNT(*) FROM tags GROUP BY namespace
                UNION ALL SELECT 'claims', COUNT(*) FROM claims
                ORDER BY 1)
            """;
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
