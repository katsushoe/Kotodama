using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kotodama.Tests;

public sealed class StructuredKnowledgeTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kotodama-structure-{Guid.NewGuid():N}.db");
    private readonly TestClock _clock = new();
    private KnowledgeStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new(_path, _clock);
        await _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
        return Task.CompletedTask;
    }

    private static StructuredKnowledgeInput Example(string text = "A resembles B") => new(text,
        [new("a", "A"), new("b", "B")], [new("a", "b", "similar_to", Confidence: .9, Strength: .7)]);

    [Fact]
    public async Task Remember_WhenStructured_PreservesOriginalAndLinksEveryClaim()
    {
        var input = Example("  A resembles B  ") with { Source = new("document", Uri: "urn:test:document", Reliability: .8) };
        var result = await _store.RememberStructuredKnowledgeAsync(input);
        var claim = (await _store.QueryClaimsAsync(relationType: "similar_to")).Should().ContainSingle().Subject;
        result.Ok.Should().BeTrue();
        result.StructureStatus.Should().Be("structured");
        result.CreatedEntities.Should().Be(4);
        result.ClaimIds.Should().Equal(claim.ClaimId);
        result.EntityIds["a"].Should().Be(claim.SubjectId);
        claim.Confidence.Should().Be(.9);
        claim.Strength.Should().Be(.7);
        claim.SourceStatementId.Should().Be(result.StatementId);
        (await _store.GetEntityAsync(result.StatementId))!.CanonicalName.Should().Be(input.Statement);
        await using var db = new SqliteConnection($"Data Source={_path}");
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT uri FROM sources WHERE id=$id";
        command.Parameters.AddWithValue("$id", claim.SourceId);
        (await command.ExecuteScalarAsync()).Should().Be("urn:test:document");
    }

    [Fact]
    public async Task Remember_WhenRetried_ReconfirmsWithoutDuplicatesAndPreservesConflict()
    {
        var first = await _store.RememberStructuredKnowledgeAsync(Example());
        _clock.UtcNow += TimeSpan.FromDays(2);
        var second = await _store.RememberStructuredKnowledgeAsync(Example());
        second.Status.Should().Be("already_stored");
        second.ClaimIds.Should().Equal(first.ClaimIds);
        second.CreatedEntities.Should().Be(0);
        (await _store.QueryClaimsAsync(relationType: "similar_to"))[0].LastConfirmedAt.Should().Be(_clock.UtcNow);
        await _store.RememberStructuredKnowledgeAsync(Example() with { Relations = [new("a", "b", "similar_to", Polarity.Negative, Strength: .7)] });
        (await _store.QueryClaimsAsync(relationType: "similar_to")).Select(x => x.Polarity).Should().BeEquivalentTo([Polarity.Positive, Polarity.Negative]);
    }

    [Fact]
    public async Task Remember_WhenLegacyStatementIsEnriched_AddsGraphOnce()
    {
        var old = await _store.RememberKnowledgeAsync(new("A resembles B"));
        var updated = await _store.RememberStructuredKnowledgeAsync(Example());
        updated.StatementId.Should().Be(old.StatementId);
        updated.Status.Should().Be("stored");
        updated.ClaimIds.Should().HaveCount(1);
        (await _store.QueryClaimsAsync(relationType: "remembers")).Should().HaveCount(1);
    }

    [Fact]
    public async Task Remember_WhenArraysAreEmpty_RequiresReasonAndProvidesGuidance()
    {
        var result = await _store.RememberStructuredKnowledgeAsync(new("Fact", [], []));
        result.Ok.Should().BeFalse();
        result.Reason.Should().Contain("2 entities").And.Contain("1 relation");
        (await _store.SearchEntitiesAsync("")).Should().BeEmpty();
        var skipped = await _store.RememberStructuredKnowledgeAsync(new("Fact", [], [], Reason: "No concepts apply"));
        skipped.Ok.Should().BeTrue();
        skipped.StructureStatus.Should().Be("skipped");
    }

    [Fact]
    public async Task Remember_WhenInvalidRelation_RollsBackWholeRequestUntilFinalRetry()
    {
        var input = Example() with { Relations = [new("a", "b", "undefined")], RetryCount = 2 };
        var rejected = await _store.RememberStructuredKnowledgeAsync(input);
        rejected.Status.Should().Be("rejected");
        (await _store.SearchEntitiesAsync("")).Should().BeEmpty();
        var fallback = await _store.RememberStructuredKnowledgeAsync(input with
        {
            RetryCount = 3,
            Event = new("actor", "visit", "place", _clock.UtcNow, _clock.UtcNow.AddHours(1))
        });
        fallback.Ok.Should().BeTrue();
        fallback.StructureStatus.Should().Be("fallback");
        fallback.Reason.Should().Contain("undefined");
        fallback.EventId.Should().BeNull();
        fallback.EntityIds.Should().BeEmpty();
        (await _store.SearchEntitiesAsync("")).Should().HaveCount(2);
        (await _store.QueryEventsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Remember_WhenKeysOrLimitsInvalid_RejectsWithoutSaving()
    {
        var duplicate = await _store.RememberStructuredKnowledgeAsync(Example() with { Entities = [new("a", "A"), new("a", "B")] });
        duplicate.Reason.Should().Contain("unique");
        var reference = await _store.RememberStructuredKnowledgeAsync(Example() with { Relations = [new("a", "missing", "similar_to")] });
        reference.Ok.Should().BeFalse();
        var oversized = await _store.RememberStructuredKnowledgeAsync(Example() with { Entities = Enumerable.Range(0, 101).Select(i => new RememberedEntityInput($"e{i}", $"E{i}")).ToArray() });
        oversized.Reason.Should().Contain("100");
        (await _store.QueryClaimsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Remember_WhenExistingEntitySpecified_ValidatesIdentityAndNamespace()
    {
        var a = await _store.CreateEntityAsync(new("A"));
        var result = await _store.RememberStructuredKnowledgeAsync(Example() with { Entities = [new("a", "A", EntityId: a.Id), new("b", "B")] });
        result.EntityIds["a"].Should().Be(a.Id);
        var invalid = await _store.RememberStructuredKnowledgeAsync(Example("wrong") with { Namespace = "other", Entities = [new("a", "A", EntityId: a.Id), new("b", "B")] });
        invalid.Ok.Should().BeFalse();
        (await _store.SearchEntitiesAsync("wrong")).Should().BeEmpty();
    }

    [Fact]
    public async Task Equality_WhenChained_IsReflexiveSymmetricTransitiveAndRetractable()
    {
        var a = await _store.CreateEntityAsync(new("A"));
        var b = await _store.CreateEntityAsync(new("B"));
        var c = await _store.CreateEntityAsync(new("C"));
        (await _store.GetEquivalentEntitiesAsync(a.Id)).Select(x => x.Id).Should().Equal(a.Id);
        (await _store.ProposeClaimAsync(new(a.Id, a.Id, "equals"))).Ok.Should().BeTrue();
        await _store.ProposeClaimAsync(new(a.Id, b.Id, "equals"));
        var bc = await _store.ProposeClaimAsync(new(b.Id, c.Id, "canonical_of"));
        (await _store.GetEquivalentEntitiesAsync(c.Id)).Select(x => x.Id).Should().BeEquivalentTo([a.Id, b.Id, c.Id]);
        (await _store.QueryClaimsAsync(relationType: "canonical_of")).Should().HaveCount(3);
        await _store.RetractClaimAsync(bc.Id!.Value);
        (await _store.GetEquivalentEntitiesAsync(c.Id)).Select(x => x.Id).Should().Equal(c.Id);
        (await _store.GetEquivalentEntitiesAsync(99999)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("equals")]
    [InlineData("canonical_of")]
    public async Task Equality_WhenNegative_RejectsBothEntryPoints(string relationType)
    {
        var a = await _store.CreateEntityAsync(new("A"));
        var b = await _store.CreateEntityAsync(new("B"));
        (await _store.ProposeClaimAsync(new(a.Id, b.Id, relationType, Polarity.Negative))).Ok.Should().BeFalse();
        var input = Example() with { Relations = [new("a", "b", relationType, Polarity.Negative)] };
        (await _store.RememberStructuredKnowledgeAsync(input)).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task SemanticClaims_WhenCrossNamespace_Rejects()
    {
        var a = await _store.CreateEntityAsync(new("A", Namespace: "one"));
        var b = await _store.CreateEntityAsync(new("B", Namespace: "two"));
        (await _store.ProposeClaimAsync(new(a.Id, b.Id, "equals"))).Ok.Should().BeFalse();
        (await _store.ProposeClaimAsync(new(a.Id, b.Id, "similar_to"))).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Search_WhenSimilarityChainExists_ReturnsCandidatesWithoutInferringClaims()
    {
        var input = new StructuredKnowledgeInput("Chain", [new("a", "Alpha"), new("b", "Beta"), new("c", "Gamma")],
            [new("a", "b", "similar_to"), new("b", "c", "similar_to"), new("a", "c", "similar_to", Polarity.Negative)]);
        var stored = await _store.RememberStructuredKnowledgeAsync(input);
        var results = await _store.SearchEntitiesAsync("Alpha");
        results.Select(x => x.CanonicalName).Should().Equal("Alpha", "Beta", "Gamma");
        results[2].Match!.Kind.Should().Be("related_path");
        results[2].Match!.ClaimIds.Should().HaveCount(2);
        (await _store.QueryClaimsAsync(relationType: "similar_to")).Should().HaveCount(3);
        (await _store.SearchEntitiesAsync("Alpha", includeRelated: false)).Should().HaveCount(1);
        (await _store.SearchEntitiesAsync("Alpha", limit: 2)).Should().HaveCount(2);
        await _store.RetractClaimAsync(stored.ClaimIds[1]);
        (await _store.SearchEntitiesAsync("Alpha")).Select(x => x.CanonicalName).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public async Task Dream_WhenSimilarityExpires_RemovesItFromSearch()
    {
        await _store.RememberStructuredKnowledgeAsync(Example());
        _clock.UtcNow += TimeSpan.FromDays(31);
        var result = await _store.RunDreamAsync();
        result.MarkedStale.Should().Be(1);
        (await _store.QueryClaimsAsync(relationType: "similar_to")).Should().BeEmpty();
        (await _store.SearchEntitiesAsync("A", includeRelated: false)).Should().BeEquivalentTo(await _store.SearchEntitiesAsync("A"), options => options.Excluding(x => x.Match));
    }

    [Fact]
    public async Task Search_WhenClaimHasFutureValidity_DoesNotExpand()
    {
        await _store.RememberStructuredKnowledgeAsync(Example() with { ValidFrom = _clock.UtcNow.AddDays(1), ValidTo = _clock.UtcNow.AddDays(2) });
        (await _store.SearchEntitiesAsync("A", includeRelated: false)).Should().BeEquivalentTo(await _store.SearchEntitiesAsync("A"), options => options.Excluding(x => x.Match));
    }

    [Theory]
    [InlineData(null, .5)]
    [InlineData("invalid", .5)]
    [InlineData("{}", .5)]
    [InlineData("[]", .5)]
    [InlineData("{\"threshold\":2}", .5)]
    [InlineData("{\"threshold\":\"0.8\"}", .5)]
    [InlineData("{\"threshold\":0.8,\"other\":1}", .5)]
    [InlineData("{\"threshold\":0.8}", .8)]
    [InlineData("{\"threshold\":0}", 0)]
    [InlineData("{\"threshold\":1}", 1)]
    public async Task Group_WhenMetadataIsProvided_NormalizesThreshold(string? metadata, double expected)
    {
        var group = await _store.CreateEntityAsync(new("Group", "SimilarityGroup", Metadata: metadata));
        using var json = System.Text.Json.JsonDocument.Parse(group.Metadata!);
        json.RootElement.GetProperty("threshold").GetDouble().Should().Be(expected);
    }

    [Fact]
    public async Task Merge_WhenMembersOverlap_UsesWeightedThresholdAndUniqueMembership()
    {
        var input = new StructuredKnowledgeInput("Groups",
            [new("a", "A"), new("b", "B"), new("g1", "G1", "SimilarityGroup", Metadata: "{\"threshold\":0.2}"), new("g2", "G2", "SimilarityGroup", Metadata: "{\"threshold\":0.8}")],
            [new("a", "g1", "member_of"), new("b", "g1", "member_of"), new("b", "g2", "member_of")]);
        var stored = await _store.RememberStructuredKnowledgeAsync(input);
        var merged = await _store.MergeSimilarityGroupsAsync(stored.EntityIds["g1"], stored.EntityIds["g2"]);
        merged.Threshold.Should().BeApproximately(.4, 1e-10);
        merged.MemberCount.Should().Be(2);
        (await _store.QueryClaimsAsync(relationType: "member_of")).Should().HaveCount(2).And.OnlyContain(x => x.ObjectId == merged.GroupId);
        (await _store.QueryClaimsAsync(relationType: "member_of", includeRetracted: true)).Should().HaveCount(5);
        (await _store.SearchEntitiesAsync("A")).Select(x => x.Id).Should().Contain(stored.EntityIds["b"]);
    }

    [Fact]
    public async Task Merge_WhenGroupsEmpty_UsesDefaultAndRejectsExistingName()
    {
        var a = await _store.CreateEntityAsync(new("G1", "SimilarityGroup"));
        var b = await _store.CreateEntityAsync(new("G2", "SimilarityGroup"));
        var invalid = () => _store.MergeSimilarityGroupsAsync(a.Id, b.Id, "G1");
        await invalid.Should().ThrowAsync<ArgumentException>();
        var result = await _store.MergeSimilarityGroupsAsync(a.Id, b.Id);
        result.Threshold.Should().Be(.5);
        result.MemberCount.Should().Be(0);
    }

    [Fact]
    public async Task Initialize_WhenRepeated_PreservesGraphAndSources()
    {
        var result = await _store.RememberStructuredKnowledgeAsync(Example());
        var reopened = new KnowledgeStore(_path, _clock);
        await reopened.InitializeAsync();
        var claims = await reopened.QueryClaimsAsync(relationType: "similar_to");
        claims.Should().ContainSingle(x => x.SourceStatementId == result.StatementId);
        (await reopened.SearchEntitiesAsync("A")).Select(x => x.CanonicalName).Should().Contain("B");
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-04T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    [Fact]
    public async Task Initialize_WhenLegacySchemaExists_MigratesWithoutLosingData()
    {
        var remembered = await _store.RememberKnowledgeAsync(new("Legacy statement"));
        var a = await _store.CreateEntityAsync(new("A"));
        var b = await _store.CreateEntityAsync(new("B"));
        await _store.ProposeClaimAsync(new(a.Id, b.Id, "similar_to"));
        await using (var db = new SqliteConnection($"Data Source={_path}"))
        {
            await db.OpenAsync();
            await using var command = db.CreateCommand();
            command.CommandText = """
                DROP INDEX idx_sources_statement;
                ALTER TABLE sources DROP COLUMN source_statement_id;
                CREATE TABLE old_symmetric(relation_id INTEGER PRIMARY KEY REFERENCES relations(id),entity_a_id INTEGER NOT NULL REFERENCES entities(id),entity_b_id INTEGER NOT NULL REFERENCES entities(id),CHECK(entity_a_id<entity_b_id),UNIQUE(entity_a_id,entity_b_id,relation_id));
                INSERT INTO old_symmetric SELECT * FROM symmetric_relations;
                DROP TABLE symmetric_relations;
                ALTER TABLE old_symmetric RENAME TO symmetric_relations;
                """;
            await command.ExecuteNonQueryAsync();
        }
        await _store.InitializeAsync();
        (await _store.GetEntityAsync(remembered.StatementId))!.CanonicalName.Should().Be("Legacy statement");
        (await _store.QueryClaimsAsync(relationType: "similar_to")).Should().HaveCount(1);
        (await _store.ProposeClaimAsync(new(a.Id, a.Id, "equals"))).Ok.Should().BeTrue();
        var structured = await _store.RememberStructuredKnowledgeAsync(Example());
        (await _store.QueryClaimsAsync(relationType: "similar_to")).Should().Contain(x => x.SourceStatementId == structured.StatementId);
    }

    [Fact]
    public async Task ReservedVocabulary_WhenUpdatedOrAliased_CannotBypassNegativeConstraint()
    {
        long typeId;
        await using (var db = new SqliteConnection($"Data Source={_path}"))
        {
            await db.OpenAsync();
            await using var command = db.CreateCommand();
            command.CommandText = "SELECT id FROM relation_types WHERE canonical_name='equals'";
            typeId = (long)(await command.ExecuteScalarAsync())!;
            command.CommandText = "INSERT INTO relation_type_aliases VALUES($id,'same_as')";
            command.Parameters.AddWithValue("$id", typeId);
            await command.ExecuteNonQueryAsync();
        }
        (await _store.UpdateRelationTypeAsync(typeId, new("loose", "other"))).Ok.Should().BeFalse();
        (await _store.DeleteRelationTypeAsync(typeId)).Ok.Should().BeFalse();
        var create = () => _store.CreateRelationTypeAsync(new("equals", "other", RelationKind.Directed));
        await create.Should().ThrowAsync<ArgumentException>();
        var a = await _store.CreateEntityAsync(new("A"));
        var b = await _store.CreateEntityAsync(new("B"));
        (await _store.ProposeClaimAsync(new(a.Id, b.Id, "same_as", Polarity.Negative))).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Remember_WhenConcurrent_StoresOneGraph()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => _store.RememberStructuredKnowledgeAsync(Example()))));
        results.Select(x => x.StatementId).Distinct().Should().HaveCount(1);
        results.SelectMany(x => x.ClaimIds).Distinct().Should().HaveCount(1);
        (await _store.QueryClaimsAsync(relationType: "similar_to")).Should().HaveCount(1);
    }

    [Fact]
    public async Task Initialize_WhenCanonicalOfIsAnExistingType_ReportsVocabularyConflict()
    {
        await using (var db = new SqliteConnection($"Data Source={_path}"))
        {
            await db.OpenAsync();
            await using var command = db.CreateCommand();
            command.CommandText = """
                INSERT INTO relation_types(canonical_name,category,directionality,allow_strength,freshness_policy,created_at,updated_at)
                VALUES('canonical_of','other','directed',0,'permanent','2026-09-04','2026-09-04')
                """;
            await command.ExecuteNonQueryAsync();
        }
        var initialize = () => _store.InitializeAsync();
        await initialize.Should().ThrowAsync<InvalidOperationException>().WithMessage("*canonical_of*alias*");
    }

    [Fact]
    public async Task Reactivate_WhenLegacyEqualityIsNegative_Rejects()
    {
        var a = await _store.CreateEntityAsync(new("A"));
        var b = await _store.CreateEntityAsync(new("B"));
        var claim = await _store.ProposeClaimAsync(new(a.Id, b.Id, "equals"));
        await using (var db = new SqliteConnection($"Data Source={_path}"))
        {
            await db.OpenAsync();
            await using var command = db.CreateCommand();
            command.CommandText = "UPDATE claims SET polarity='negative',status='retracted' WHERE id=$id";
            command.Parameters.AddWithValue("$id", claim.Id);
            await command.ExecuteNonQueryAsync();
        }
        (await _store.ReactivateClaimAsync(claim.Id!.Value)).Status.Should().Be("rejected");
        (await _store.QueryClaimsAsync(relationType: "equals")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task Remember_WhenNumbersAreNotFinite_Rejects(double value)
    {
        var input = Example() with { Relations = [new("a", "b", "similar_to", Strength: value)] };
        (await _store.RememberStructuredKnowledgeAsync(input)).Ok.Should().BeFalse();
        (await _store.QueryClaimsAsync()).Should().BeEmpty();
    }
}
