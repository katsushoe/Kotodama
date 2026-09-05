using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Kotodama;

public sealed partial class KnowledgeStore
{
    internal const double DefaultSimilarityThreshold = 0.5;
    internal const int MaximumRememberEntities = 100;
    internal const int MaximumRememberRelations = 200;
    internal const int MaximumStructureRetries = 3;

    private static readonly RelationTypeInput[] SemanticTypes =
    [
        new("similar_to", "semantic", RelationKind.Symmetric, true, FreshnessPolicy: FreshnessPolicy.Periodic, RefreshAfterSeconds: RememberRefreshAfterSeconds),
        new("equals", "identity", RelationKind.Symmetric),
        new("member_of", "classification", RelationKind.Directed)
    ];

    /// <summary>構造化知識を保存します。入力修正は呼び出し側が最大3回まで行います。</summary>
    public Task<RememberKnowledgeResult> RememberStructuredKnowledgeAsync(StructuredKnowledgeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.RetryCount is < 0 or > MaximumStructureRetries) throw new ArgumentOutOfRangeException(nameof(input), "retryCount must be between 0 and 3");
        // 配列の欠落はAPI契約違反であり、明示的な空配列とは区別します。
        ArgumentNullException.ThrowIfNull(input.Entities);
        ArgumentNullException.ThrowIfNull(input.Relations);
        return RememberKnowledgeAsync(new(input.Statement, input.Namespace, input.Confidence, input.Source, input.ObservedAt, input.ValidFrom, input.ValidTo, input.Event, input.Tags), cancellationToken, input);
    }

    private async Task<RememberKnowledgeResult> CompleteRememberAsync(SqliteConnection connection, SqliteTransaction transaction,
        StructuredKnowledgeInput? input, RememberKnowledgeResult result, CancellationToken token, IReadOnlyList<string>? tags, string entityNamespace)
    {
        if (input is null)
        {
            if (await ApplyRememberTagsAsync(connection, transaction, result, tags, entityNamespace, token)) result = result with { Status = "stored" };
            await transaction.CommitAsync(token);
            return result;
        }

        transaction.Save("structure");
        try
        {
            ValidateStructure(input);
            var (entities, created) = await PersistConceptsAsync(connection, transaction, input, token);
            var (claims, createdClaims) = await PersistExtractedClaimsAsync(connection, transaction, input, result, entities, token);
            var eventId = await PersistRememberedEventAsync(connection, transaction, result.StatementId, input.Statement, input.Namespace, input.Event, Now(), token);
            result = result with
            {
                Status = created > 0 || createdClaims > 0 ? "stored" : result.Status,
                CreatedEntities = result.CreatedEntities + created,
                EntityIds = entities,
                EventId = eventId,
                ClaimIds = claims,
                StructureStatus = input.Entities.Count == 0 || input.Relations.Count == 0 ? "skipped" : "structured",
                Reason = input.Reason
            };
            transaction.Release("structure");
        }
        catch (ArgumentException error)
        {
            // 契約上の入力エラーだけを返却/縮退し、DB障害やキャンセルは伝播します。
            transaction.Rollback("structure");
            transaction.Release("structure");
            if (input.RetryCount < MaximumStructureRetries)
                return new(false, "rejected", 0, 0, 0, 0, false) { StructureStatus = "rejected", Reason = error.Message };
            result = result with { StructureStatus = "fallback", Reason = error.Message };
        }
        if (await ApplyRememberTagsAsync(connection, transaction, result, tags, entityNamespace, token)) result = result with { Status = "stored" };
        await transaction.CommitAsync(token);
        return result;
    }

    private static void ValidateStructure(StructuredKnowledgeInput input)
    {
        if ((input.Entities.Count == 0 || input.Relations.Count == 0) && string.IsNullOrWhiteSpace(input.Reason))
            throw new ArgumentException("Provide approximately at least 2 entities and 1 relation; intentional zero counts require reason. Retry with corrected structure at most 3 times.");
        if (input.Entities.Count > MaximumRememberEntities || input.Relations.Count > MaximumRememberRelations)
            throw new ArgumentException("At most 100 entities and 200 relations are allowed.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in input.Entities)
        {
            if (entity is null || string.IsNullOrWhiteSpace(entity.Key) || string.IsNullOrWhiteSpace(entity.CanonicalName) || string.IsNullOrWhiteSpace(entity.ClassName))
                throw new ArgumentException("Each entity requires key, canonicalName and className.");
            if (!keys.Add(entity.Key)) throw new ArgumentException("Entity keys must be unique.");
        }
        foreach (var relation in input.Relations)
        {
            if (relation is null || string.IsNullOrWhiteSpace(relation.RelationType) || !keys.Contains(relation.Subject) || !keys.Contains(relation.Object))
                throw new ArgumentException("Each relation requires relationType and subject/object keys from entities.");
        }
    }

    private async Task<(Dictionary<string, long> Ids, int Created)> PersistConceptsAsync(SqliteConnection connection, SqliteTransaction transaction,
        StructuredKnowledgeInput input, CancellationToken token)
    {
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        var created = 0;
        foreach (var entity in input.Entities)
        {
            if (entity.EntityId is long existingId)
            {
                var existing = await ReadEntityAsync(connection, transaction, existingId, token);
                if (existing is null || existing.Namespace != input.Namespace || existing.ClassName != entity.ClassName || existing.CanonicalName != entity.CanonicalName.Trim())
                    throw new ArgumentException("entityId must match canonicalName, className and namespace.");
                ids.Add(entity.Key, existingId);
                continue;
            }
            var (id, isNew) = await GetOrCreateEntityAsync(connection, transaction, entity.CanonicalName.Trim(), entity.ClassName, input.Namespace, Now(), token);
            if (isNew)
            {
                created++;
                await SetMetadataAsync(connection, transaction, id, NormalizeMetadata(entity.ClassName, entity.Metadata), token);
            }
            ids.Add(entity.Key, id);
        }
        return (ids, created);
    }

    private async Task<(List<long> Ids, int Created)> PersistExtractedClaimsAsync(SqliteConnection connection, SqliteTransaction transaction,
        StructuredKnowledgeInput input, RememberKnowledgeResult statement, Dictionary<string, long> entities, CancellationToken token)
    {
        var ids = new List<long>();
        var created = 0;
        foreach (var edge in input.Relations)
        {
            var type = await GetRelationTypeAsync(connection, transaction, edge.RelationType, token)
                ?? throw new ArgumentException($"relation_type not found: {edge.RelationType}; create it before retrying.");
            var source = (input.Source ?? new SourceInput("user_message")) with { SourceStatementId = statement.StatementId };
            var candidate = new ClaimCandidate(entities[edge.Subject], entities[edge.Object], type.Name, edge.Polarity, edge.Confidence,
                Strength: edge.Strength, KnowledgeSubjectId: statement.SubjectId, Source: source, AssertionType: "extracted",
                ObservedAt: input.ObservedAt, ValidFrom: input.ValidFrom, ValidTo: input.ValidTo, LastConfirmedAt: input.ObservedAt ?? Now());
            var error = KnowledgeRules.Validate(candidate, type.AllowStrength)
                ?? await ValidateSemanticClaimAsync(connection, transaction, candidate, type.Kind, token);
            if (error is not null) throw new ArgumentException(error);
            var relationId = await GetOrCreateRelationAsync(connection, transaction, type.Id, type.Kind, candidate.SubjectId, candidate.ObjectId, token);
            var claimId = await FindExtractedClaimAsync(connection, transaction, relationId, statement.StatementId, candidate, token);
            if (claimId is long existing)
                await ReconfirmRememberedClaimAsync(connection, transaction, existing, candidate.Confidence, Now(), token);
            else
            {
                var sourceId = await InsertSourceAsync(connection, transaction, source, token);
                claimId = await InsertClaimAsync(connection, transaction, relationId, sourceId, candidate, token);
                created++;
            }
            if (!ids.Contains(claimId.Value)) ids.Add(claimId.Value);
        }
        return (ids, created);
    }

    private static async Task<long?> FindExtractedClaimAsync(SqliteConnection connection, SqliteTransaction transaction, long relationId,
        long statementId, ClaimCandidate candidate, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT c.id FROM claims c JOIN sources src ON src.id=c.source_id
            WHERE c.relation_id=$relation AND src.source_statement_id=$statement
              AND c.assertion_type='extracted' AND c.polarity=$polarity AND c.status<>'retracted'
              AND c.strength IS $strength AND c.valid_from IS $from AND c.valid_to IS $to
            ORDER BY c.id LIMIT 1
            """;
        command.Parameters.AddWithValue("$relation", relationId);
        command.Parameters.AddWithValue("$statement", statementId);
        command.Parameters.AddWithValue("$polarity", Lower(candidate.Polarity));
        command.Parameters.AddWithValue("$strength", (object?)candidate.Strength ?? DBNull.Value);
        command.Parameters.AddWithValue("$from", candidate.ValidFrom is null ? DBNull.Value : Format(candidate.ValidFrom.Value));
        command.Parameters.AddWithValue("$to", candidate.ValidTo is null ? DBNull.Value : Format(candidate.ValidTo.Value));
        return await command.ExecuteScalarAsync(token) is long id ? id : null;
    }

    private static async Task<string?> ValidateSemanticClaimAsync(SqliteConnection connection, SqliteTransaction transaction,
        ClaimCandidate candidate, RelationKind kind, CancellationToken token)
    {
        if (kind == RelationKind.Symmetric && candidate.SubjectId == candidate.ObjectId && candidate.RelationType != "equals")
            return "Only equals supports explicit reflexive claims.";
        if (candidate.RelationType is not ("similar_to" or "equals" or "member_of")) return null;
        var subject = await ReadEntityAsync(connection, transaction, candidate.SubjectId, token);
        var obj = await ReadEntityAsync(connection, transaction, candidate.ObjectId, token);
        if (subject is null || obj is null) return "entity not found";
        if (candidate.RelationType == "member_of" && obj.ClassName != "SimilarityGroup") return null;
        if (subject.Namespace != obj.Namespace) return "Semantic links cannot cross namespaces.";
        if (candidate.RelationType == "member_of" && subject.ClassName == "SimilarityGroup") return "SimilarityGroup members must be concepts, not groups.";
        return null;
    }

    private static async Task<EntityRecord?> ReadEntityAsync(SqliteConnection connection, SqliteTransaction transaction, long id, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,canonical_name,class_name,namespace,metadata,created_at,updated_at FROM entities WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadEntity(reader) : null;
    }

    private static async Task SetMetadataAsync(SqliteConnection connection, SqliteTransaction transaction, long id, string? metadata, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE entities SET metadata=$metadata WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$metadata", (object?)metadata ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token);
    }

    private static string? NormalizeMetadata(string className, string? metadata) => className == "SimilarityGroup"
        ? JsonSerializer.Serialize(new { threshold = ReadThreshold(metadata) }) : metadata;

    private static double ReadThreshold(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return DefaultSimilarityThreshold;
        // TryParseValueにより、仕様どおり不正JSONも既定値として扱います。
        var bytes = System.Text.Encoding.UTF8.GetBytes(metadata);
        var reader = new Utf8JsonReader(bytes);
        try
        {
            if (!JsonDocument.TryParseValue(ref reader, out var document)) return DefaultSimilarityThreshold;
            using (document)
            {
                var root = document.RootElement;
                if (reader.BytesConsumed != bytes.Length && reader.Read()) return DefaultSimilarityThreshold;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1) return DefaultSimilarityThreshold;
                return root.TryGetProperty("threshold", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var threshold)
                    && double.IsFinite(threshold) && threshold is >= 0 and <= 1 ? threshold : DefaultSimilarityThreshold;
            }
        }
        catch (JsonException)
        {
            // 不正metadataはエラーでなく0.5へ正規化する公開契約です。
            return DefaultSimilarityThreshold;
        }
    }

    private async Task InitializeStructuredKnowledgeAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var column = connection.CreateCommand();
        column.Transaction = transaction;
        column.CommandText = "SELECT COUNT(*) FROM pragma_table_info('sources') WHERE name='source_statement_id'";
        if (Convert.ToInt32(await column.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == 0)
        {
            column.CommandText = "ALTER TABLE sources ADD COLUMN source_statement_id INTEGER REFERENCES entities(id)";
            await column.ExecuteNonQueryAsync(token);
        }
        column.CommandText = "SELECT sql FROM sqlite_master WHERE name='symmetric_relations'";
        var definition = (string?)await column.ExecuteScalarAsync(token);
        if (definition?.Contains("entity_a_id<entity_b_id", StringComparison.Ordinal) == true)
        {
            column.CommandText = """
                CREATE TABLE symmetric_relations_v2(relation_id INTEGER PRIMARY KEY REFERENCES relations(id),entity_a_id INTEGER NOT NULL REFERENCES entities(id),entity_b_id INTEGER NOT NULL REFERENCES entities(id),CHECK(entity_a_id<=entity_b_id),UNIQUE(entity_a_id,entity_b_id,relation_id));
                INSERT INTO symmetric_relations_v2 SELECT * FROM symmetric_relations;
                DROP TABLE symmetric_relations;
                ALTER TABLE symmetric_relations_v2 RENAME TO symmetric_relations;
                CREATE INDEX idx_symmetric_a ON symmetric_relations(entity_a_id);
                CREATE INDEX idx_symmetric_b ON symmetric_relations(entity_b_id);
                """;
            await column.ExecuteNonQueryAsync(token);
        }
        column.CommandText = "SELECT COUNT(*) FROM relation_types WHERE canonical_name='canonical_of'";
        if (Convert.ToInt32(await column.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0)
            throw new InvalidOperationException("canonical_of is reserved as an alias of equals; review the existing relation type before upgrading.");
        foreach (var type in SemanticTypes) await EnsureSemanticTypeAsync(connection, transaction, type, token);
        column.CommandText = """
            INSERT OR IGNORE INTO relation_type_aliases(relation_type_id,alias)
            SELECT id,'canonical_of' FROM relation_types WHERE canonical_name='equals';
            CREATE INDEX IF NOT EXISTS idx_sources_statement ON sources(source_statement_id);
            """;
        await column.ExecuteNonQueryAsync(token);
        var alias = await GetRelationTypeAsync(connection, transaction, "canonical_of", token);
        if (alias?.Name != "equals") throw new InvalidOperationException("canonical_of conflicts with the reserved equals alias.");
        await transaction.CommitAsync(token);
    }

    private async Task EnsureSemanticTypeAsync(SqliteConnection connection, SqliteTransaction transaction, RelationTypeInput type, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO relation_types(canonical_name,category,directionality,allow_strength,freshness_policy,refresh_after_seconds,created_at,updated_at)
            VALUES($name,$category,$kind,$strength,$freshness,$refresh,$now,$now);
            """;
        command.Parameters.AddWithValue("$name", type.CanonicalName);
        command.Parameters.AddWithValue("$category", type.Category);
        command.Parameters.AddWithValue("$kind", Lower(type.Kind));
        command.Parameters.AddWithValue("$strength", type.AllowStrength ? 1 : 0);
        command.Parameters.AddWithValue("$freshness", Lower(type.FreshnessPolicy));
        command.Parameters.AddWithValue("$refresh", (object?)type.RefreshAfterSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Format(Now()));
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "SELECT COUNT(*) FROM relation_types WHERE canonical_name=$name AND category=$category AND directionality=$kind AND allow_strength=$strength AND freshness_policy=$freshness AND refresh_after_seconds IS $refresh";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException($"Existing relation type {type.CanonicalName} conflicts with the reserved vocabulary; review it before upgrading.");
    }

    private static bool IsReservedRelation(string name) => name is "similar_to" or "equals" or "canonical_of" or "member_of";
}
