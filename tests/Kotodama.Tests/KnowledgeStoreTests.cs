using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class KnowledgeStoreTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kotodama-{Guid.NewGuid():N}.db");
    private KnowledgeStore _store = null!;

    public async Task InitializeAsync() { _store = new KnowledgeStore(_path, TimeProvider.System); await _store.InitializeAsync(); }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(_path)) File.Delete(_path); return Task.CompletedTask; }

    [Fact]
    public async Task ProposeClaim_WhenClaimsConflict_PreservesBoth()
    {
        var a = await _store.CreateEntityAsync(new("A")); var b = await _store.CreateEntityAsync(new("B"));
        await _store.CreateRelationTypeAsync(new("parent_of", "social", RelationKind.Directed));
        (await _store.ProposeClaimAsync(new(a.Id, b.Id, "parent_of", Polarity.Positive, .8))).Ok.Should().BeTrue();
        (await _store.ProposeClaimAsync(new(a.Id, b.Id, "parent_of", Polarity.Negative, .7))).Ok.Should().BeTrue();
        var claims = await _store.QueryClaimsAsync(a.Id);
        claims.Should().HaveCount(2); claims.Select(x => x.Polarity).Should().Contain([Polarity.Positive, Polarity.Negative]);
    }

    [Fact]
    public async Task ProposeClaim_WhenStrengthDisallowed_IsRejected()
    {
        var a = await _store.CreateEntityAsync(new("A")); var b = await _store.CreateEntityAsync(new("B"));
        await _store.CreateRelationTypeAsync(new("parent_of", "social", RelationKind.Directed));
        var result = await _store.ProposeClaimAsync(new(a.Id, b.Id, "parent_of", Strength: .5));
        result.Ok.Should().BeFalse(); result.Status.Should().Be("rejected");
    }

    [Fact]
    public async Task SymmetricRelation_WhenOrderReversed_ReusesRelation()
    {
        var a = await _store.CreateEntityAsync(new("A")); var b = await _store.CreateEntityAsync(new("B"));
        await _store.CreateRelationTypeAsync(new("friend_of", "social", RelationKind.Symmetric));
        await _store.ProposeClaimAsync(new(a.Id, b.Id, "friend_of")); await _store.ProposeClaimAsync(new(b.Id, a.Id, "friend_of"));
        var claims = await _store.QueryClaimsAsync(a.Id);
        claims.Should().HaveCount(2); claims.Select(x => x.RelationId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task QueryClaims_WhenAbsent_ReturnsUnknownAsEmpty()
    {
        (await _store.QueryClaimsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RememberKnowledge_WhenTextIsNew_StoresStructuredStatement()
    {
        var result = await _store.RememberKnowledgeAsync(new("定例バックアップは毎週金曜日の18時に実行します。"));
        var statement = await _store.GetEntityAsync(result.StatementId);
        var claims = await _store.QueryClaimsAsync(result.SubjectId, "remembers");

        result.Ok.Should().BeTrue();
        result.Status.Should().Be("stored");
        result.CreatedEntities.Should().Be(2);
        result.CreatedRelationType.Should().BeTrue();
        statement.Should().NotBeNull();
        statement!.ClassName.Should().Be("Statement");
        claims.Should().ContainSingle(x => x.ClaimId == result.ClaimId && x.AssertionType == "remembered_text");
    }

    [Fact]
    public async Task RememberKnowledge_WhenActiveTextAlreadyExists_DoesNotDuplicateClaim()
    {
        var first = await _store.RememberKnowledgeAsync(new("同じ知識"));
        var second = await _store.RememberKnowledgeAsync(new("  同じ知識  "));
        var claims = await _store.QueryClaimsAsync(first.SubjectId, "remembers");

        second.Status.Should().Be("already_stored");
        second.ClaimId.Should().Be(first.ClaimId);
        second.CreatedEntities.Should().Be(0);
        second.CreatedRelationType.Should().BeFalse();
        claims.Should().ContainSingle();
    }

    [Fact]
    public async Task RememberKnowledge_WhenInputIsInvalid_DoesNotCreateRows()
    {
        var action = () => _store.RememberKnowledgeAsync(new("invalid", Confidence: 2));

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await _store.SearchEntitiesAsync(string.Empty)).Should().BeEmpty();
        (await _store.QueryClaimsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateEvent_WithLiteralObject_PersistsEventEntity()
    {
        var actor = await _store.CreateEntityAsync(new("佐藤", "Person"));
        var occurred = DateTimeOffset.Parse("2026-08-25T10:00:00+09:00");
        var result = await _store.CreateEventAsync(new("見積送信", actor.Id, occurred, "send", ObjectValue: "見積書"));
        var entity = await _store.GetEntityAsync(result.EntityId);
        entity.Should().NotBeNull(); entity!.ClassName.Should().Be("Event");
    }

    [Fact]
    public async Task RunDream_WhenPeriodicClaimsExist_StagesAllAndMarksOnlyExpiredStale()
    {
        var now = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
        var store = new KnowledgeStore(_path, new FixedTimeProvider(now), DreamTempStore.Memory);
        var a = await store.CreateEntityAsync(new("DreamA"));
        var b = await store.CreateEntityAsync(new("DreamB"));
        await store.CreateRelationTypeAsync(new("works_at", "organizational", RelationKind.Directed, FreshnessPolicy: FreshnessPolicy.Periodic, RefreshAfterSeconds: 60));
        await store.ProposeClaimAsync(new(a.Id, b.Id, "works_at", ObservedAt: now.AddMinutes(-2)));
        await store.ProposeClaimAsync(new(a.Id, b.Id, "works_at", ObservedAt: now.AddSeconds(-30)));

        var result = await store.RunDreamAsync();
        var claims = await store.QueryClaimsAsync(a.Id, includeStale: true);

        result.Examined.Should().Be(2);
        result.MarkedStale.Should().Be(1);
        claims.Count(x => x.Status == ClaimStatus.Stale).Should().Be(1);
        claims.Count(x => x.Status == ClaimStatus.Active).Should().Be(1);
    }

    [Fact]
    public async Task RunDream_WhenRememberedKnowledgeAges_ReducesConfidenceGradually()
    {
        var now = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
        var time = new MutableTimeProvider(now);
        var store = new KnowledgeStore(_path, time, DreamTempStore.Memory);
        var remembered = await store.RememberKnowledgeAsync(new("徐々に薄れる知識"));
        time.Advance(TimeSpan.FromSeconds(KnowledgeStore.RememberRefreshAfterSeconds + 1));

        var first = await store.RunDreamAsync();
        var claim = (await store.QueryClaimsAsync(remembered.SubjectId, "remembers")).Single();
        var second = await store.RunDreamAsync();

        first.ReducedConfidence.Should().Be(1);
        first.MarkedStale.Should().Be(0);
        claim.Confidence.Should().BeApproximately(KnowledgeStore.RememberDecayFactor, 0.000001);
        claim.Status.Should().Be(ClaimStatus.Active);
        second.ReducedConfidence.Should().Be(0);
    }

    [Fact]
    public async Task RunDream_WhenRememberedKnowledgeKeepsAging_EventuallyMarksStaleAndHidesByDefault()
    {
        var now = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
        var time = new MutableTimeProvider(now);
        var store = new KnowledgeStore(_path, time, DreamTempStore.Memory);
        var remembered = await store.RememberKnowledgeAsync(new("忘却対象の知識"));

        for (var index = 0; index < 8; index++)
        {
            time.Advance(TimeSpan.FromSeconds(KnowledgeStore.RememberRefreshAfterSeconds + 1));
            await store.RunDreamAsync();
        }

        (await store.QueryClaimsAsync(remembered.SubjectId, "remembers")).Should().BeEmpty();
        var stale = (await store.QueryClaimsAsync(remembered.SubjectId, "remembers", includeStale: true)).Single();
        stale.Status.Should().Be(ClaimStatus.Stale);
        stale.Confidence.Should().BeLessThan(KnowledgeStore.RememberStaleThreshold);
    }

    [Fact]
    public async Task RememberKnowledge_WhenAgedKnowledgeIsRepeated_RestoresConfidenceAndConfirmation()
    {
        var now = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
        var time = new MutableTimeProvider(now);
        var store = new KnowledgeStore(_path, time, DreamTempStore.Memory);
        var first = await store.RememberKnowledgeAsync(new("再確認する知識"));
        for (var index = 0; index < 8; index++)
        {
            time.Advance(TimeSpan.FromSeconds(KnowledgeStore.RememberRefreshAfterSeconds + 1));
            await store.RunDreamAsync();
        }

        (await store.QueryClaimsAsync(first.SubjectId, "remembers")).Should().BeEmpty();
        time.Advance(TimeSpan.FromDays(1));

        var repeated = await store.RememberKnowledgeAsync(new("再確認する知識"));
        var claim = (await store.QueryClaimsAsync(first.SubjectId, "remembers")).Single();

        repeated.Status.Should().Be("already_stored");
        repeated.ClaimId.Should().Be(first.ClaimId);
        claim.Confidence.Should().Be(1);
        claim.LastConfirmedAt.Should().Be(time.GetUtcNow());
    }

    [Fact]
    public async Task Initialize_WhenLegacyRememberTypeIsPermanent_MigratesToPeriodicDecay()
    {
        await _store.RememberKnowledgeAsync(new("移行対象の知識"));
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _path }.ToString();
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE relation_types SET freshness_policy='permanent',refresh_after_seconds=NULL WHERE canonical_name='remembers'";
            await update.ExecuteNonQueryAsync();
        }

        await _store.InitializeAsync();

        await using var migratedConnection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await migratedConnection.OpenAsync();
        await using var query = migratedConnection.CreateCommand();
        query.CommandText = "SELECT freshness_policy,refresh_after_seconds FROM relation_types WHERE canonical_name='remembers'";
        await using var reader = await query.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("periodic");
        reader.GetInt64(1).Should().Be(KnowledgeStore.RememberRefreshAfterSeconds);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
