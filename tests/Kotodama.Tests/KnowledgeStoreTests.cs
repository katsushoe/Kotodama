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
        var claims = await store.QueryClaimsAsync(a.Id);

        result.Examined.Should().Be(2);
        result.MarkedStale.Should().Be(1);
        claims.Count(x => x.Status == ClaimStatus.Stale).Should().Be(1);
        claims.Count(x => x.Status == ClaimStatus.Active).Should().Be(1);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
