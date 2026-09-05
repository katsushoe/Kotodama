using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kotodama.Tests;

public sealed class KnowledgeTagTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kotodama-tags-{Guid.NewGuid():N}.db");
    private KnowledgeStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new(_path, TimeProvider.System);
        await _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
        return Task.CompletedTask;
    }

    private static StructuredKnowledgeInput Example(string text, params string[] tags) => new(text,
        [new("a", "A"), new("b", "B")], [new("a", "b", "similar_to", Strength: .7)], Tags: tags);

    [Fact]
    public async Task Remember_WithTags_InheritsWithoutTextMatchAndReusesNames()
    {
        var first = await _store.RememberStructuredKnowledgeAsync(Example("北の城で育った", "ミルラッド年代記", " ＡＢＣ ", "abc"));
        (await _store.ListTagsAsync()).Should().HaveCount(2);
        var statements = await _store.QueryTaggedStatementsAsync(new(Tags: ["ミルラッド年代記"]));
        statements.Should().ContainSingle().Which.Statement.Id.Should().Be(first.StatementId);
        statements[0].Tags.Should().OnlyContain(x => x.Origin == "remember");
        var claims = await _store.QueryTaggedClaimsAsync(new(Tags: ["abc"]));
        claims.Select(x => x.Claim.ClaimId).Should().BeEquivalentTo(first.ClaimIds.Append(first.ClaimId));
        claims.SelectMany(x => x.Tags).Should().OnlyContain(x => x.Origin == "inherited" && x.SourceStatementId == first.StatementId);
        var again = await _store.RememberStructuredKnowledgeAsync(Example("北の城で育った", "abc", "ミルラッド年代記"));
        again.Status.Should().Be("already_stored");
        (await _store.ListTagsAsync()).Should().HaveCount(2);
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["abc"]))).Should().ContainSingle();
    }

    [Fact]
    public async Task Search_WithAnyAllUnknownAndAliases_ChangesResultSets()
    {
        var a = await _store.RememberStructuredKnowledgeAsync(Example("first", "one", "two"));
        await _store.RememberStructuredKnowledgeAsync(Example("second", "one"));
        await _store.RememberStructuredKnowledgeAsync(Example("third", "two"));
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["one", "two"]))).Should().HaveCount(3);
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["one", "two"], TagMatch: "all"))).Should().ContainSingle().Which.Statement.Id.Should().Be(a.StatementId);
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["one", "two"]))).Should().HaveCount(6);
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["one", "two"], TagMatch: "all"))).Should().HaveCount(2);
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["one", "unknown"], TagMatch: "all"))).Should().BeEmpty();
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["one", "unknown"]))).Should().HaveCount(2);
        var tag = await _store.CreateTagAsync("one");
        await _store.AddTagAliasAsync(tag.Id, "alias");
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["one", "alias"], TagIds: [tag.Id], TagMatch: "all"))).Should().HaveCount(2);
    }

    [Fact]
    public async Task Names_WithUnicodeNamespaceAndCollision_KeepExplicitBoundaries()
    {
        var tag = await _store.CreateTagAsync(" é ");
        (await _store.CreateTagAsync("e\u0301")).Id.Should().Be(tag.Id);
        (await _store.CreateTagAsync("É", "other")).Id.Should().NotBe(tag.Id);
        var another = await _store.CreateTagAsync("other");
        await _store.Invoking(x => x.AddTagAliasAsync(another.Id, "É")).Should().ThrowAsync<ArgumentException>();
        await _store.Invoking(x => x.RenameTagAsync(another.Id, "é")).Should().ThrowAsync<ArgumentException>();
        (await _store.ListTagsAsync()).Single(x => x.Id == another.Id).Name.Should().Be("other");
        await _store.Invoking(x => x.QueryTaggedStatementsAsync(new(TagIds: [tag.Id], Namespace: "other"))).Should().ThrowAsync<ArgumentException>();
        var stored = await _store.RememberStructuredKnowledgeAsync(Example("private", "é") with { Namespace = "other" });
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["é"]))).Should().BeEmpty();
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["é"], Namespace: "other"))).Should().ContainSingle().Which.Statement.Id.Should().Be(stored.StatementId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("　 ")]
    [InlineData("tag\nname")]
    public async Task Names_WhenInvalid_RejectBeforePersistence(string name)
    {
        await _store.Invoking(x => x.RememberStructuredKnowledgeAsync(Example("invalid tag", name))).Should().ThrowAsync<ArgumentException>();
        (await _store.ListTagsAsync()).Should().BeEmpty();
        (await _store.SearchEntitiesAsync("invalid tag")).Should().BeEmpty();
    }

    [Fact]
    public async Task Remember_WhenStructureRejectedOrFallback_TagsFollowTransactionOutcome()
    {
        var input = Example("retry", "one") with { Relations = [new("a", "b", "missing")] };
        (await _store.RememberStructuredKnowledgeAsync(input)).Status.Should().Be("rejected");
        (await _store.ListTagsAsync()).Should().BeEmpty();
        (await _store.SearchEntitiesAsync("retry")).Should().BeEmpty();
        var fallback = await _store.RememberStructuredKnowledgeAsync(input with { RetryCount = 3 });
        fallback.StructureStatus.Should().Be("fallback");
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["one"]))).Should().ContainSingle().Which.Claim.ClaimId.Should().Be(fallback.ClaimId);
    }

    [Fact]
    public async Task SetTags_WithPreviewAndExpectedCount_UpdatesAndRemovesOnlySelectedTargets()
    {
        var old = await _store.RememberKnowledgeAsync(new("legacy"));
        await _store.RememberStructuredKnowledgeAsync(Example("new"));
        var tag = await _store.CreateTagAsync("project");
        var bulk = new SetKnowledgeTagsInput("statement", [tag.Id], KnowledgeSubjectId: old.SubjectId);
        (await _store.SetKnowledgeTagsAsync(bulk)).Should().Be(new TagUpdateResult(2, 0, true));
        (await _store.QueryTaggedStatementsAsync(new(TagIds: [tag.Id]))).Should().BeEmpty();
        await _store.Invoking(x => x.SetKnowledgeTagsAsync(bulk with { DryRun = false, ExpectedCount = 1 })).Should().ThrowAsync<ArgumentException>();
        (await _store.QueryTaggedStatementsAsync(new(TagIds: [tag.Id]))).Should().BeEmpty();
        (await _store.SetKnowledgeTagsAsync(bulk with { DryRun = false, ExpectedCount = 2 })).ChangedCount.Should().Be(2);
        (await _store.SetKnowledgeTagsAsync(bulk with { DryRun = false, ExpectedCount = 2 })).ChangedCount.Should().Be(0);
        (await _store.QueryTaggedStatementsAsync(new(TagIds: [tag.Id]))).SelectMany(x => x.Tags).Should().OnlyContain(x => x.Origin == "manual");
        (await _store.QueryTaggedClaimsAsync(new(TagIds: [tag.Id]))).Should().BeEmpty();
        var single = new SetKnowledgeTagsInput("statement", [tag.Id], [old.StatementId], Remove: true, DryRun: false, ExpectedCount: 1);
        (await _store.SetKnowledgeTagsAsync(single)).ChangedCount.Should().Be(1);
        (await _store.QueryTaggedStatementsAsync(new(TagIds: [tag.Id]))).Should().ContainSingle();
        var claims = bulk with { TargetKind = "claim", DryRun = false, ExpectedCount = 3 };
        (await _store.SetKnowledgeTagsAsync(claims)).ChangedCount.Should().Be(3);
        (await _store.QueryTaggedClaimsAsync(new(TagIds: [tag.Id]))).Should().HaveCount(3);
        (await _store.SetKnowledgeTagsAsync(new("claim", [tag.Id], [old.ClaimId], Remove: true, DryRun: false, ExpectedCount: 1))).ChangedCount.Should().Be(1);
        (await _store.QueryTaggedClaimsAsync(new(TagIds: [tag.Id]))).Should().HaveCount(2);
    }

    [Fact]
    public async Task SetTags_WithWrongNamespaceOrMissingTarget_RollsBackAll()
    {
        var saved = await _store.RememberKnowledgeAsync(new("other", Namespace: "other"));
        var tag = await _store.CreateTagAsync("one");
        await _store.Invoking(x => x.SetKnowledgeTagsAsync(new("statement", [tag.Id], [saved.StatementId], DryRun: false, ExpectedCount: 1))).Should().ThrowAsync<ArgumentException>();
        var local = await _store.RememberKnowledgeAsync(new("local"));
        await _store.Invoking(x => x.SetKnowledgeTagsAsync(new("claim", [tag.Id], [local.ClaimId, long.MaxValue], DryRun: false, ExpectedCount: 2))).Should().ThrowAsync<ArgumentException>();
        (await _store.QueryTaggedClaimsAsync(new(TagIds: [tag.Id]))).Should().BeEmpty();
    }

    [Fact]
    public async Task Merge_AfterRenameAndAlias_PreservesAssociationsAndOldIds()
    {
        await _store.RememberStructuredKnowledgeAsync(Example("first", "old"));
        await _store.RememberStructuredKnowledgeAsync(Example("second", "target", "old"));
        var old = await _store.CreateTagAsync("old");
        var target = await _store.CreateTagAsync("target");
        (await _store.RenameTagAsync(old.Id, "new")).Id.Should().Be(old.Id);
        await _store.AddTagAliasAsync(old.Id, "alias");
        await _store.MergeTagsAsync(old.Id, target.Id);
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["old", "new", "alias"], TagMatch: "all"))).Should().HaveCount(2);
        (await _store.QueryTaggedClaimsAsync(new(TagIds: [old.Id, target.Id], TagMatch: "all"))).Should().HaveCount(4);
        var statements = await _store.QueryTaggedStatementsAsync(new(TagIds: [target.Id]));
        statements.SelectMany(x => x.Tags).Should().HaveCount(2).And.OnlyContain(x => x.TagId == target.Id);
        (await _store.ListTagsAsync()).Single(x => x.Id == old.Id).MergedIntoId.Should().Be(target.Id);
        (await _store.CreateTagAsync("alias")).Id.Should().Be(target.Id);
        (await _store.MergeTagsAsync(target.Id, old.Id)).Id.Should().Be(target.Id);
        var next = await _store.CreateTagAsync("next");
        await _store.MergeTagsAsync(target.Id, next.Id);
        (await _store.QueryTaggedClaimsAsync(new(TagIds: [old.Id]))).Should().HaveCount(4);
        var external = await _store.CreateTagAsync("external", "other");
        await _store.Invoking(x => x.MergeTagsAsync(old.Id, external.Id)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Claims_WithStateTimeDeletionAndPagination_UseExistingSemantics()
    {
        var saved = await _store.RememberStructuredKnowledgeAsync(Example("first", "one"));
        var second = await _store.RememberStructuredKnowledgeAsync(Example("second", "one"));
        var page = await _store.QueryTaggedStatementsAsync(new(Tags: ["one"], Limit: 1));
        page.Should().ContainSingle().Which.Statement.Id.Should().Be(saved.StatementId);
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["one"], AfterId: saved.StatementId))).Should().ContainSingle().Which.Statement.Id.Should().Be(second.StatementId);
        await _store.RetractClaimAsync(saved.ClaimIds[0]);
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["one"]))).Should().HaveCount(3);
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["one"], IncludeRetracted: true))).Should().HaveCount(4);
        await _store.DeleteClaimAsync(saved.ClaimIds[0]);
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["one"], IncludeRetracted: true))).Should().HaveCount(3);
        var now = DateTimeOffset.UtcNow;
        await _store.RememberStructuredKnowledgeAsync(Example("time", "timed") with { ValidFrom = now, ValidTo = now.AddHours(1) });
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["timed"], ValidAt: now))).Should().HaveCount(2);
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["timed"], ValidAt: now.AddHours(1)))).Should().BeEmpty();
    }

    [Fact]
    public async Task Initialize_OnLegacyDatabase_IsIdempotentAndPreservesExistingData()
    {
        var saved = await _store.RememberKnowledgeAsync(new("legacy text"));
        await using (var db = new SqliteConnection($"Data Source={_path}"))
        {
            await db.OpenAsync();
            await using var command = db.CreateCommand();
            command.CommandText = "DROP TABLE claim_tags; DROP TABLE statement_tags; DROP TABLE tag_names; DROP TABLE tags;";
            await command.ExecuteNonQueryAsync();
        }
        await _store.InitializeAsync();
        await _store.InitializeAsync();
        (await _store.GetEntityAsync(saved.StatementId))!.CanonicalName.Should().Be("legacy text");
        (await _store.QueryClaimsAsync()).Should().ContainSingle();
        var enriched = await _store.RememberStructuredKnowledgeAsync(Example("legacy text", "new"));
        enriched.StatementId.Should().Be(saved.StatementId);
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["new"]))).Should().HaveCount(2);
        await _store.InitializeAsync();
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["new"]))).Should().ContainSingle();
    }

    [Fact]
    public async Task Remember_WhenConcurrent_DoesNotDuplicateNormalizedTag()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Task.Run(async () => { await start.Task; return await _store.RememberStructuredKnowledgeAsync(Example("first", "ＡＢＣ")); });
        var second = Task.Run(async () => { await start.Task; return await _store.RememberStructuredKnowledgeAsync(Example("second", "abc")); });
        start.SetResult();
        await Task.WhenAll(first, second);
        (await _store.ListTagsAsync()).Should().ContainSingle();
        (await _store.QueryTaggedStatementsAsync(new(Tags: ["ABC"]))).Should().HaveCount(2);
    }

    [Fact]
    public async Task Remember_WhenTagDatabaseWriteFails_RollsBackStatementGraphAndTags()
    {
        await using (var db = new SqliteConnection($"Data Source={_path}"))
        {
            await db.OpenAsync();
            await using var command = db.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_tag BEFORE INSERT ON claim_tags BEGIN SELECT RAISE(ABORT,'injected failure'); END";
            await command.ExecuteNonQueryAsync();
        }
        await _store.Invoking(x => x.RememberStructuredKnowledgeAsync(Example("failed", "one") with { RetryCount = 3 })).Should().ThrowAsync<SqliteException>();
        (await _store.ListTagsAsync()).Should().BeEmpty();
        (await _store.SearchEntitiesAsync("")).Should().BeEmpty();
        (await _store.QueryClaimsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Backup_AfterTagsAndMerge_RestoresAllAssociations()
    {
        var backup = _path + ".backup";
        try
        {
            await _store.RememberStructuredKnowledgeAsync(Example("backup", "one"));
            var one = await _store.CreateTagAsync("one");
            var two = await _store.CreateTagAsync("two");
            await _store.MergeTagsAsync(one.Id, two.Id);
            await _store.BackupAsync(backup);
            var restored = new KnowledgeStore(backup, TimeProvider.System);
            await restored.InitializeAsync();
            (await restored.QueryTaggedStatementsAsync(new(TagIds: [one.Id]))).Should().ContainSingle();
            (await restored.QueryTaggedClaimsAsync(new(Tags: ["one"]))).Should().HaveCount(2);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(backup);
        }
    }

    [Fact]
    public async Task Search_WhenClaimIsStale_RequiresExplicitInclusion()
    {
        var saved = await _store.RememberStructuredKnowledgeAsync(Example("stale", "one"));
        await using (var db = new SqliteConnection($"Data Source={_path}"))
        {
            await db.OpenAsync();
            await using var command = db.CreateCommand();
            command.CommandText = "UPDATE claims SET status='stale' WHERE id=$id";
            command.Parameters.AddWithValue("$id", saved.ClaimIds[0]);
            await command.ExecuteNonQueryAsync();
        }
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["one"]))).Should().ContainSingle().Which.Claim.ClaimId.Should().Be(saved.ClaimId);
        (await _store.QueryTaggedClaimsAsync(new(Tags: ["one"], IncludeStale: true))).Should().HaveCount(2);
    }

    [Fact]
    public async Task Inputs_WithEmptySelectionOrInvalidBounds_AreRejected()
    {
        await _store.Invoking(x => x.QueryTaggedStatementsAsync(new())).Should().ThrowAsync<ArgumentException>();
        await _store.Invoking(x => x.QueryTaggedStatementsAsync(new(Tags: ["one"], Limit: 0))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await _store.Invoking(x => x.QueryTaggedClaimsAsync(new(Tags: ["one"], AfterId: -1))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await _store.Invoking(x => x.CreateTagAsync(new string('a', 129))).Should().ThrowAsync<ArgumentException>();
        await _store.Invoking(x => x.RememberStructuredKnowledgeAsync(Example("too many", Enumerable.Repeat("one", 101).ToArray()))).Should().ThrowAsync<ArgumentException>();
        await _store.Invoking(x => x.SetKnowledgeTagsAsync(new("statement", [1], []))).Should().ThrowAsync<ArgumentException>();
        await _store.Invoking(x => x.SetKnowledgeTagsAsync(new("statement", [1], [1], KnowledgeSubjectId: 1))).Should().ThrowAsync<ArgumentException>();
        await _store.Invoking(x => x.SetKnowledgeTagsAsync(new("statement", [1], [1], DryRun: false))).Should().ThrowAsync<ArgumentException>();
    }
}
