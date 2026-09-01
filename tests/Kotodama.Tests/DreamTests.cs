using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class DreamTests : IAsyncLifetime
{
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kotodama-dream-{Guid.NewGuid():N}.db");
    private KnowledgeStore _store = null!;
    private long _subjectId;
    private long _objectId;

    public async Task InitializeAsync()
    {
        _store = new(_path, new FixedTimeProvider(_now));
        await _store.InitializeAsync();
        _subjectId = (await _store.CreateEntityAsync(new("Subject"))).Id;
        _objectId = (await _store.CreateEntityAsync(new("Object"))).Id;
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RunDream_WhenClaimIsExactlyAtThreshold_KeepsActive()
    {
        await CreateTypeAsync("periodic_equal", FreshnessPolicy.Periodic, 60);
        await ProposeAsync("periodic_equal", _now.AddSeconds(-60));

        var result = await _store.RunDreamAsync();

        result.Examined.Should().Be(1);
        result.MarkedStale.Should().Be(0);
        (await SingleClaimAsync()).Status.Should().Be(ClaimStatus.Active);
    }

    [Fact]
    public async Task RunDream_WhenClaimIsPastThreshold_MarksStale()
    {
        await CreateTypeAsync("periodic_expired", FreshnessPolicy.Periodic, 60);
        await ProposeAsync("periodic_expired", _now.AddSeconds(-61));

        var result = await _store.RunDreamAsync();

        result.MarkedStale.Should().Be(1);
        (await SingleClaimAsync()).Status.Should().Be(ClaimStatus.Stale);
    }

    [Fact]
    public async Task RunDream_WhenLastConfirmedIsRecent_UsesItInsteadOfObservedAt()
    {
        await CreateTypeAsync("confirmed_recently", FreshnessPolicy.Periodic, 60);
        await ProposeAsync("confirmed_recently", _now.AddDays(-1), _now.AddSeconds(-30));

        var result = await _store.RunDreamAsync();

        result.MarkedStale.Should().Be(0);
        (await SingleClaimAsync()).Status.Should().Be(ClaimStatus.Active);
    }

    [Fact]
    public async Task RunDream_WhenPermanentClaimIsOld_DoesNotExamineIt()
    {
        await CreateTypeAsync("permanent", FreshnessPolicy.Permanent, 1);
        await ProposeAsync("permanent", _now.AddYears(-1));

        var result = await _store.RunDreamAsync();

        result.Should().Be(new DreamResult(0, 0, _now));
        (await SingleClaimAsync()).Status.Should().Be(ClaimStatus.Active);
    }

    [Fact]
    public async Task RunDream_WhenVolatileClaimExpires_MarksStale()
    {
        await CreateTypeAsync("volatile", FreshnessPolicy.Volatile, 10);
        await ProposeAsync("volatile", _now.AddSeconds(-11));

        (await _store.RunDreamAsync()).MarkedStale.Should().Be(1);
    }

    [Fact]
    public async Task RunDream_WhenRunTwice_IsIdempotent()
    {
        await CreateTypeAsync("idempotent", FreshnessPolicy.Periodic, 10);
        await ProposeAsync("idempotent", _now.AddSeconds(-11));

        var first = await _store.RunDreamAsync();
        var second = await _store.RunDreamAsync();

        first.MarkedStale.Should().Be(1);
        second.Examined.Should().Be(0);
        second.MarkedStale.Should().Be(0);
    }

    [Fact]
    public async Task RunDream_WhenClaimWasRetracted_DoesNotExamineIt()
    {
        await CreateTypeAsync("retracted", FreshnessPolicy.Periodic, 10);
        var claim = await ProposeAsync("retracted", _now.AddSeconds(-11));
        await _store.RetractClaimAsync(claim);

        var result = await _store.RunDreamAsync();

        result.Examined.Should().Be(0);
        (await SingleClaimAsync(includeRetracted: true)).Status.Should().Be(ClaimStatus.Retracted);
    }

    [Fact]
    public async Task RunDream_WhenNoClaimsExist_ReturnsZeroCounts()
    {
        (await _store.RunDreamAsync()).Should().Be(new DreamResult(0, 0, _now));
    }

    [Theory]
    [InlineData(DreamTempStore.Default)]
    [InlineData(DreamTempStore.Memory)]
    [InlineData(DreamTempStore.File)]
    public async Task RunDream_WithEachTempStore_ProducesSameResult(DreamTempStore tempStore)
    {
        var store = new KnowledgeStore(_path, new FixedTimeProvider(_now), tempStore);
        await CreateTypeAsync("temp_store_" + tempStore, FreshnessPolicy.Periodic, 10);
        await store.ProposeClaimAsync(new(_subjectId, _objectId, "temp_store_" + tempStore, ObservedAt: _now.AddSeconds(-11)));

        var result = await store.RunDreamAsync();

        result.MarkedStale.Should().Be(1);
    }

    private Task CreateTypeAsync(string name, FreshnessPolicy policy, long refresh) =>
        _store.CreateRelationTypeAsync(new(name, "state", RelationKind.Directed, FreshnessPolicy: policy, RefreshAfterSeconds: refresh));

    private async Task<long> ProposeAsync(string type, DateTimeOffset observedAt, DateTimeOffset? confirmedAt = null)
    {
        var result = await _store.ProposeClaimAsync(new(_subjectId, _objectId, type, ObservedAt: observedAt, LastConfirmedAt: confirmedAt));
        result.Ok.Should().BeTrue();
        return result.Id!.Value;
    }

    private async Task<ClaimRecord> SingleClaimAsync(bool includeRetracted = false) =>
        (await _store.QueryClaimsAsync(includeRetracted: includeRetracted, includeStale: true)).Single();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
