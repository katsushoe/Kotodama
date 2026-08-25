using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class DreamConcurrencyTests : IAsyncLifetime
{
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kotodama-concurrency-{Guid.NewGuid():N}.db");
    private KnowledgeStore _store = null!;
    private long _subjectId;
    private long _objectId;

    public async Task InitializeAsync()
    {
        _store = new(_path, new FixedTimeProvider(_now));
        await _store.InitializeAsync();
        _subjectId = (await _store.CreateEntityAsync(new("ConcurrentSubject"))).Id;
        _objectId = (await _store.CreateEntityAsync(new("ConcurrentObject"))).Id;
        await _store.CreateRelationTypeAsync(new("concurrent_relation", "state", RelationKind.Directed, FreshnessPolicy: FreshnessPolicy.Periodic, RefreshAfterSeconds: 10));
        await _store.ProposeClaimAsync(new(_subjectId, _objectId, "concurrent_relation", ObservedAt: _now.AddSeconds(-11)));
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RunDream_WhenClaimIsRetractedAfterStaging_DoesNotOverwriteRetraction()
    {
        var hook = new PausingHook(DreamPausePoint.AfterStaging);
        var dreamStore = CreateStore(hook);
        var dreamTask = dreamStore.RunDreamAsync();
        await hook.WaitUntilPausedAsync();

        var claim = (await _store.QueryClaimsAsync()).Single();
        await _store.RetractClaimAsync(claim.ClaimId);
        hook.Release();
        var result = await dreamTask;

        result.MarkedStale.Should().Be(0);
        (await _store.QueryClaimsAsync(includeRetracted: true)).Single().Status.Should().Be(ClaimStatus.Retracted);
    }

    [Fact]
    public async Task RunDream_WhileUpdateIsUncommitted_ReadersSeeOldStateUntilCommit()
    {
        var hook = new PausingHook(DreamPausePoint.AfterUpdate);
        var dreamStore = CreateStore(hook);
        var dreamTask = dreamStore.RunDreamAsync();
        await hook.WaitUntilPausedAsync();

        (await _store.QueryClaimsAsync()).Single().Status.Should().Be(ClaimStatus.Active);
        hook.Release();
        await dreamTask;

        (await _store.QueryClaimsAsync()).Single().Status.Should().Be(ClaimStatus.Stale);
    }

    [Fact]
    public async Task RunDream_WhenTwoExecutionsRace_OnlyOnePublishesTransition()
    {
        var gate = new SharedStagingGate(2);
        var first = CreateStore(gate).RunDreamAsync();
        var second = CreateStore(gate).RunDreamAsync();

        var results = await Task.WhenAll(first, second);

        results.Sum(x => x.MarkedStale).Should().Be(1);
        (await _store.QueryClaimsAsync()).Single().Status.Should().Be(ClaimStatus.Stale);
    }

    [Fact]
    public async Task RunDream_WhenFailureOccursAfterStaging_LeavesOnlineClaimUnchanged()
    {
        var failingStore = CreateStore(new ThrowingHook(DreamPausePoint.AfterStaging));

        var action = () => failingStore.RunDreamAsync();

        await action.Should().ThrowAsync<InjectedDreamException>();
        (await _store.QueryClaimsAsync()).Single().Status.Should().Be(ClaimStatus.Active);
        (await _store.RunDreamAsync()).MarkedStale.Should().Be(1);
    }

    [Fact]
    public async Task RunDream_WhenFailureOccursAfterUpdate_RollsBackAndCanRetry()
    {
        var failingStore = CreateStore(new ThrowingHook(DreamPausePoint.AfterUpdate));

        var action = () => failingStore.RunDreamAsync();

        await action.Should().ThrowAsync<InjectedDreamException>();
        (await _store.QueryClaimsAsync()).Single().Status.Should().Be(ClaimStatus.Active);
        (await _store.RunDreamAsync()).MarkedStale.Should().Be(1);
    }

    private KnowledgeStore CreateStore(IDreamExecutionHook hook) => new(_path, new FixedTimeProvider(_now), DreamTempStore.Memory, hook);

    private enum DreamPausePoint { AfterStaging, AfterUpdate }

    private sealed class PausingHook(DreamPausePoint point) : IDreamExecutionHook
    {
        private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AfterStagingAsync(CancellationToken cancellationToken) => PauseAsync(DreamPausePoint.AfterStaging, cancellationToken);
        public Task AfterUpdateAsync(CancellationToken cancellationToken) => PauseAsync(DreamPausePoint.AfterUpdate, cancellationToken);
        public Task WaitUntilPausedAsync() => _paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public void Release() => _released.TrySetResult();

        private async Task PauseAsync(DreamPausePoint current, CancellationToken token)
        {
            if (current != point) return;
            _paused.TrySetResult();
            await _released.Task.WaitAsync(token);
        }
    }

    private sealed class SharedStagingGate(int participants) : IDreamExecutionHook
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remaining = participants;

        public async Task AfterStagingAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _remaining) == 0) _released.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken);
        }

        public Task AfterUpdateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThrowingHook(DreamPausePoint point) : IDreamExecutionHook
    {
        public Task AfterStagingAsync(CancellationToken cancellationToken) => ThrowIf(DreamPausePoint.AfterStaging);
        public Task AfterUpdateAsync(CancellationToken cancellationToken) => ThrowIf(DreamPausePoint.AfterUpdate);
        private Task ThrowIf(DreamPausePoint current) => current == point ? Task.FromException(new InjectedDreamException()) : Task.CompletedTask;
    }

    private sealed class InjectedDreamException : Exception;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
