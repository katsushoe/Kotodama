using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class KnowledgeStoreAdministrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kotodama-admin-{Guid.NewGuid():N}");
    private KnowledgeStore _store = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _store = new KnowledgeStore(Path.Combine(_root, "source.db"), TimeProvider.System);
        await _store.InitializeAsync();
    }

    [Fact]
    public async Task RelationType_UpdateAndDelete_WhenUnused_Succeeds()
    {
        var id = await _store.CreateRelationTypeAsync(new("old_name", "old", RelationKind.Directed));
        var updated = await _store.UpdateRelationTypeAsync(id, new("new_name", "new", FreshnessPolicy: FreshnessPolicy.Periodic, RefreshAfterSeconds: 60));
        var deleted = await _store.DeleteRelationTypeAsync(id);

        updated.Should().Be(new OperationResult(true, "updated", Id: id));
        deleted.Should().Be(new OperationResult(true, "deleted", Id: id));
    }

    [Fact]
    public async Task DeleteRelationType_WhenUsed_IsRejected()
    {
        var id = await _store.CreateRelationTypeAsync(new("used", "test", RelationKind.Directed));
        var subject = await _store.CreateEntityAsync(new("subject"));
        var obj = await _store.CreateEntityAsync(new("object"));
        await _store.ProposeClaimAsync(new(subject.Id, obj.Id, "used"));

        (await _store.DeleteRelationTypeAsync(id)).Status.Should().Be("in_use");
    }

    [Fact]
    public async Task Claim_ReactivateThenDelete_Succeeds()
    {
        await _store.CreateRelationTypeAsync(new("claim_admin", "test", RelationKind.Directed));
        var subject = await _store.CreateEntityAsync(new("subject"));
        var obj = await _store.CreateEntityAsync(new("object"));
        var claim = await _store.ProposeClaimAsync(new(subject.Id, obj.Id, "claim_admin"));
        await _store.RetractClaimAsync(claim.Id!.Value);

        (await _store.ReactivateClaimAsync(claim.Id.Value)).Status.Should().Be("reactivated");
        (await _store.DeleteClaimAsync(claim.Id.Value)).Status.Should().Be("deleted");
        (await _store.QueryClaimsAsync(includeRetracted: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task Backup_CreatesReadableCopy()
    {
        await _store.CreateEntityAsync(new("backup_entity"));
        var path = Path.Combine(_root, "backup", "copy.db");

        await _store.BackupAsync(path);

        var copy = new KnowledgeStore(path, TimeProvider.System);
        (await copy.SearchEntitiesAsync("backup_entity")).Should().ContainSingle();
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        return Task.CompletedTask;
    }
}
