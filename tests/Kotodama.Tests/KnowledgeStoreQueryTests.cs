using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class KnowledgeStoreQueryTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kotodama-query-{Guid.NewGuid():N}.db");
    private KnowledgeStore _store = null!;
    private EntityRecord _subject = null!;
    private EntityRecord _object = null!;

    public async Task InitializeAsync()
    {
        _store = new(_path, TimeProvider.System);
        await _store.InitializeAsync();
        _subject = await _store.CreateEntityAsync(new("QuerySubject", "Person", "project:test", "{}"));
        _object = await _store.CreateEntityAsync(new("QueryObject"));
        await _store.CreateRelationTypeAsync(new("related_to", "semantic", RelationKind.Directed, AllowStrength: true));
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetEntity_WhenEntityExists_ReturnsAllFields()
    {
        var result = await _store.GetEntityAsync(_subject.Id);

        result.Should().BeEquivalentTo(_subject);
    }

    [Fact]
    public async Task GetEntity_WhenEntityDoesNotExist_ReturnsNull()
    {
        (await _store.GetEntityAsync(long.MaxValue)).Should().BeNull();
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task SearchEntities_WhenQueryContainsLikeMetacharacter_TreatsItLiterally(string query)
    {
        await _store.CreateEntityAsync(new("Literal" + query + "Name"));

        var results = await _store.SearchEntitiesAsync(query);

        results.Should().ContainSingle(x => x.CanonicalName == "Literal" + query + "Name");
        results.Should().NotContain(x => x.CanonicalName == "QuerySubject");
    }

    [Fact]
    public async Task ProposeClaim_WhenRelationTypeDoesNotExist_ReturnsRejected()
    {
        var result = await _store.ProposeClaimAsync(new(_subject.Id, _object.Id, "missing"));

        result.Should().Be(new OperationResult(false, "rejected", "relation_type not found"));
    }

    [Fact]
    public async Task ProposeClaim_WhenEntityDoesNotExist_ReturnsRejected()
    {
        var result = await _store.ProposeClaimAsync(new(_subject.Id, long.MaxValue, "related_to"));

        result.Should().Be(new OperationResult(false, "rejected", "entity not found"));
    }

    [Fact]
    public async Task ProposeClaim_WithEpistemicAndSourceFields_PreservesThem()
    {
        var knower = await _store.CreateEntityAsync(new("Knower", "Person"));
        var source = new SourceInput("official_document", "https://example.test/source", "doc-1", "Document", _subject.Id, 0.95, "{}");
        var candidate = new ClaimCandidate(_subject.Id, _object.Id, "related_to", Confidence: 0.8, AttributionConfidence: 0.7, Strength: 0.6, KnowledgeSubjectId: knower.Id, Source: source, AssertionType: "reported");

        await _store.ProposeClaimAsync(candidate);
        var claim = (await _store.QueryClaimsAsync()).Single();

        claim.Confidence.Should().Be(0.8);
        claim.AttributionConfidence.Should().Be(0.7);
        claim.Strength.Should().Be(0.6);
        claim.KnowledgeSubjectId.Should().Be(knower.Id);
        claim.SourceId.Should().NotBeNull();
        claim.AssertionType.Should().Be("reported");
    }

    [Fact]
    public async Task RetractClaim_WhenActive_MovesItOutOfDefaultQuery()
    {
        var proposed = await _store.ProposeClaimAsync(new(_subject.Id, _object.Id, "related_to"));

        var result = await _store.RetractClaimAsync(proposed.Id!.Value);

        result.Status.Should().Be("retracted");
        (await _store.QueryClaimsAsync()).Should().BeEmpty();
        (await _store.QueryClaimsAsync(includeRetracted: true)).Single().Status.Should().Be(ClaimStatus.Retracted);
    }

    [Fact]
    public async Task RetractClaim_WhenAlreadyRetracted_ReturnsNotFound()
    {
        var proposed = await _store.ProposeClaimAsync(new(_subject.Id, _object.Id, "related_to"));
        await _store.RetractClaimAsync(proposed.Id!.Value);

        var result = await _store.RetractClaimAsync(proposed.Id.Value);

        result.Should().Be(new OperationResult(false, "not_found", "active claim not found"));
    }

    [Fact]
    public async Task QueryClaims_AtValidFrom_IncludesClaim()
    {
        var from = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        await _store.ProposeClaimAsync(new(_subject.Id, _object.Id, "related_to", ValidFrom: from, ValidTo: from.AddDays(1)));

        (await _store.QueryClaimsAsync(validAt: from)).Should().ContainSingle();
    }

    [Fact]
    public async Task QueryClaims_AtValidTo_ExcludesClaim()
    {
        var to = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
        await _store.ProposeClaimAsync(new(_subject.Id, _object.Id, "related_to", ValidFrom: to.AddDays(-1), ValidTo: to));

        (await _store.QueryClaimsAsync(validAt: to)).Should().BeEmpty();
    }

    [Fact]
    public async Task QueryClaims_WhenFilteringRelationType_ReturnsOnlyMatchingType()
    {
        await _store.CreateRelationTypeAsync(new("other_type", "semantic", RelationKind.Directed));
        await _store.ProposeClaimAsync(new(_subject.Id, _object.Id, "related_to"));
        await _store.ProposeClaimAsync(new(_subject.Id, _object.Id, "other_type"));

        var results = await _store.QueryClaimsAsync(relationType: "other_type");

        results.Should().ContainSingle(x => x.RelationType == "other_type");
    }

    [Fact]
    public async Task CreateEvent_WhenObjectIsMissing_ThrowsBeforeCreatingEntity()
    {
        var before = await _store.SearchEntitiesAsync("InvalidEvent");

        var action = () => _store.CreateEventAsync(new("InvalidEvent", _subject.Id, DateTimeOffset.UtcNow, "send"));

        await action.Should().ThrowAsync<ArgumentException>();
        (await _store.SearchEntitiesAsync("InvalidEvent")).Should().HaveSameCount(before);
    }
}
