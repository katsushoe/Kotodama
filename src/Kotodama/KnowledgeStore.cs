using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Kotodama;

/// <summary>SQLite Knowledge Graph の永続化と検索を提供します。</summary>
public sealed class KnowledgeStore
{
    internal const long RememberRefreshAfterSeconds = 2_592_000;
    internal const double RememberDecayFactor = 0.8;
    internal const double RememberStaleThreshold = 0.2;

    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;
    private readonly DreamTempStore _dreamTempStore;
    private readonly IDreamExecutionHook _dreamExecutionHook;

    /// <summary>ストアを生成します。</summary>
    public KnowledgeStore(string databasePath, TimeProvider timeProvider, DreamTempStore dreamTempStore = DreamTempStore.Default)
        : this(databasePath, timeProvider, dreamTempStore, NoOpDreamExecutionHook.Instance)
    {
    }

    internal KnowledgeStore(string databasePath, TimeProvider timeProvider, DreamTempStore dreamTempStore, IDreamExecutionHook dreamExecutionHook)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(dreamExecutionHook);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true }.ToString();
        _timeProvider = timeProvider;
        _dreamTempStore = dreamTempStore;
        _dreamExecutionHook = dreamExecutionHook;
    }

    /// <summary>DBスキーマを初期化します。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = Schema;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var migration = connection.CreateCommand();
        migration.CommandText = "UPDATE relation_types SET freshness_policy='periodic',refresh_after_seconds=$refresh,updated_at=$now WHERE canonical_name='remembers' AND freshness_policy='permanent' AND refresh_after_seconds IS NULL";
        migration.Parameters.AddWithValue("$refresh", RememberRefreshAfterSeconds);
        migration.Parameters.AddWithValue("$now", Format(Now()));
        await migration.ExecuteNonQueryAsync(cancellationToken);

        await EnsureEventColumnsAsync(connection, cancellationToken);
    }

    /// <summary>Entity を登録します。</summary>
    public async Task<EntityRecord> CreateEntityAsync(EntityInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CanonicalName);
        var now = Now();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO entities(class_name,canonical_name,namespace,metadata,created_at,updated_at) VALUES($class,$name,$namespace,$metadata,$now,$now); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$class", input.ClassName);
        command.Parameters.AddWithValue("$name", input.CanonicalName);
        command.Parameters.AddWithValue("$namespace", input.Namespace);
        command.Parameters.AddWithValue("$metadata", (object?)input.Metadata ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Format(now));
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        return new(id, input.CanonicalName, input.ClassName, input.Namespace, input.Metadata, now, now);
    }

    /// <summary>RelationType を登録します。</summary>
    public async Task<long> CreateRelationTypeAsync(RelationTypeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO relation_types(canonical_name,category,directionality,allow_strength,inverse_name,freshness_policy,refresh_after_seconds,description,created_at,updated_at) VALUES($name,$category,$kind,$strength,$inverse,$freshness,$refresh,$description,$now,$now); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$name", input.CanonicalName);
        command.Parameters.AddWithValue("$category", input.Category);
        command.Parameters.AddWithValue("$kind", Lower(input.Kind));
        command.Parameters.AddWithValue("$strength", input.AllowStrength ? 1 : 0);
        command.Parameters.AddWithValue("$inverse", (object?)input.InverseName ?? DBNull.Value);
        command.Parameters.AddWithValue("$freshness", Lower(input.FreshnessPolicy));
        command.Parameters.AddWithValue("$refresh", (object?)input.RefreshAfterSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", (object?)input.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Format(Now()));
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    /// <summary>独立した事実としてEventを登録します。合成と因果は通常のRelationで表現します。</summary>
    public async Task<EventRecord> CreateEventAsync(EventInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Action);
        if (input.ObjectId is null && string.IsNullOrWhiteSpace(input.ObjectValue)) throw new ArgumentException("object_id or object_value is required", nameof(input));
        if (input.EndsAt is not null && input.EndsAt <= input.OccurredAt) throw new ArgumentException("ends_at must be greater than occurred_at", nameof(input));
        var entity = await CreateEntityAsync(new(input.CanonicalName, "Event", input.Namespace, input.Metadata), cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO events(entity_id,actor_id,occurred_at,action,object_id,object_value,ends_at,source_statement_id) VALUES($entity,$actor,$occurred,$action,$object,$value,$ends,$statement)";
        command.Parameters.AddWithValue("$entity", entity.Id);
        command.Parameters.AddWithValue("$actor", (object?)input.ActorId ?? DBNull.Value);
        command.Parameters.AddWithValue("$occurred", Format(input.OccurredAt));
        command.Parameters.AddWithValue("$action", input.Action);
        command.Parameters.AddWithValue("$object", (object?)input.ObjectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$value", (object?)input.ObjectValue ?? DBNull.Value);
        command.Parameters.AddWithValue("$ends", input.EndsAt is null ? DBNull.Value : Format(input.EndsAt.Value));
        command.Parameters.AddWithValue("$statement", (object?)input.SourceStatementId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new(entity.Id, entity.CanonicalName, input.ActorId, input.OccurredAt, input.Action, input.ObjectId, input.ObjectValue, input.EndsAt, input.SourceStatementId);
    }

    /// <summary>Knowledge Candidate を検証し、Relation と Claim を保存します。</summary>
    public async Task<OperationResult> ProposeClaimAsync(ClaimCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var type = await GetRelationTypeAsync(connection, transaction, candidate.RelationType, cancellationToken);
        if (type is null) return new(false, "rejected", "relation_type not found");
        var reason = KnowledgeRules.Validate(candidate, type.Value.AllowStrength);
        if (reason is not null) return new(false, "rejected", reason);
        if (!await EntitiesExistAsync(connection, transaction, candidate, cancellationToken)) return new(false, "rejected", "entity not found");

        var relationId = await GetOrCreateRelationAsync(connection, transaction, type.Value.Id, type.Value.Kind, candidate.SubjectId, candidate.ObjectId, cancellationToken);
        long? sourceId = candidate.Source is null ? null : await InsertSourceAsync(connection, transaction, candidate.Source, cancellationToken);
        var claimId = await InsertClaimAsync(connection, transaction, relationId, sourceId, candidate, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, "accepted", Id: claimId);
    }

    /// <summary>自然文をユーザーが主張したStatementとして原子的に保存します。</summary>
    public async Task<RememberKnowledgeResult> RememberKnowledgeAsync(RememberKnowledgeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Namespace);
        if (input.Confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(input), "confidence must be between 0 and 1");
        if (input.ValidFrom is not null && input.ValidTo is not null && input.ValidTo <= input.ValidFrom) throw new ArgumentException("valid_to must be greater than valid_from", nameof(input));
        ValidateRememberedEvent(input.Event);

        var text = input.Text.Trim();
        var now = Now();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var (subjectId, subjectCreated) = await GetOrCreateEntityAsync(connection, transaction, "Conversation user", "KnowledgeSubject", input.Namespace, now, cancellationToken);
        var (statementId, statementCreated) = await GetOrCreateEntityAsync(connection, transaction, text, "Statement", input.Namespace, now, cancellationToken);
        var (relationTypeId, relationTypeCreated) = await GetOrCreateRememberRelationTypeAsync(connection, transaction, now, cancellationToken);
        var relationId = await GetOrCreateRelationAsync(connection, transaction, relationTypeId, RelationKind.Directed, subjectId, statementId, cancellationToken);
        var existingClaimId = await FindRememberedClaimAsync(connection, transaction, relationId, cancellationToken);
        if (existingClaimId is not null)
        {
            await ReconfirmRememberedClaimAsync(connection, transaction, existingClaimId.Value, input.Confidence, now, cancellationToken);
            var existingEventId = await PersistRememberedEventAsync(connection, transaction, statementId, text, input.Namespace, input.Event, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(true, "already_stored", subjectId, statementId, existingClaimId.Value, Convert.ToInt32(subjectCreated) + Convert.ToInt32(statementCreated), relationTypeCreated, existingEventId);
        }

        var source = input.Source ?? new SourceInput("user_message");
        var sourceId = await InsertSourceAsync(connection, transaction, source, cancellationToken);
        var candidate = new ClaimCandidate(
            subjectId,
            statementId,
            "remembers",
            Confidence: input.Confidence,
            KnowledgeSubjectId: subjectId,
            Source: source,
            AssertionType: "remembered_text",
            ObservedAt: input.ObservedAt,
            ValidFrom: input.ValidFrom,
            ValidTo: input.ValidTo,
            LastConfirmedAt: input.ObservedAt ?? now);
        var claimId = await InsertClaimAsync(connection, transaction, relationId, sourceId, candidate, cancellationToken);
        var eventId = await PersistRememberedEventAsync(connection, transaction, statementId, text, input.Namespace, input.Event, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, "stored", subjectId, statementId, claimId, Convert.ToInt32(subjectCreated) + Convert.ToInt32(statementCreated), relationTypeCreated, eventId);
    }

    /// <summary>Claim を論理撤回します。</summary>
    public async Task<OperationResult> RetractClaimAsync(long claimId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE claims SET status='retracted',updated_at=$now WHERE id=$id AND status<>'retracted'";
        command.Parameters.AddWithValue("$id", claimId);
        command.Parameters.AddWithValue("$now", Format(Now()));
        var count = await command.ExecuteNonQueryAsync(cancellationToken);
        return count == 1 ? new(true, "retracted", Id: claimId) : new(false, "not_found", "active claim not found");
    }

    /// <summary>撤回またはstaleのClaimを再確認済みのactiveへ戻します。</summary>
    public async Task<OperationResult> ReactivateClaimAsync(long claimId, DateTimeOffset? confirmedAt = null, CancellationToken cancellationToken = default)
    {
        var now = confirmedAt ?? Now();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE claims SET status='active',last_confirmed_at=$now,updated_at=$now WHERE id=$id AND status<>'active'";
        command.Parameters.AddWithValue("$id", claimId);
        command.Parameters.AddWithValue("$now", Format(now));
        var count = await command.ExecuteNonQueryAsync(cancellationToken);
        return count == 1 ? new(true, "reactivated", Id: claimId) : new(false, "not_found", "inactive claim not found");
    }

    /// <summary>Claimを物理削除します。</summary>
    public async Task<OperationResult> DeleteClaimAsync(long claimId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM claims WHERE id=$id";
        command.Parameters.AddWithValue("$id", claimId);
        var count = await command.ExecuteNonQueryAsync(cancellationToken);
        return count == 1 ? new(true, "deleted", Id: claimId) : new(false, "not_found", "claim not found");
    }

    /// <summary>RelationTypeを更新します。既存Relationの方向性は変更しません。</summary>
    public async Task<OperationResult> UpdateRelationTypeAsync(long relationTypeId, RelationTypeUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CanonicalName);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE relation_types SET canonical_name=$name,category=$category,allow_strength=$strength,inverse_name=$inverse,freshness_policy=$freshness,refresh_after_seconds=$refresh,description=$description,updated_at=$now WHERE id=$id";
        command.Parameters.AddWithValue("$id", relationTypeId);
        command.Parameters.AddWithValue("$name", input.CanonicalName);
        command.Parameters.AddWithValue("$category", input.Category);
        command.Parameters.AddWithValue("$strength", input.AllowStrength ? 1 : 0);
        command.Parameters.AddWithValue("$inverse", (object?)input.InverseName ?? DBNull.Value);
        command.Parameters.AddWithValue("$freshness", Lower(input.FreshnessPolicy));
        command.Parameters.AddWithValue("$refresh", (object?)input.RefreshAfterSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", (object?)input.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Format(Now()));
        var count = await command.ExecuteNonQueryAsync(cancellationToken);
        return count == 1 ? new(true, "updated", Id: relationTypeId) : new(false, "not_found", "relation_type not found");
    }

    /// <summary>未使用のRelationTypeを物理削除します。</summary>
    public async Task<OperationResult> DeleteRelationTypeAsync(long relationTypeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM relation_types WHERE id=$id AND NOT EXISTS(SELECT 1 FROM relations WHERE relation_type_id=$id)";
        command.Parameters.AddWithValue("$id", relationTypeId);
        var count = await command.ExecuteNonQueryAsync(cancellationToken);
        if (count == 1) return new(true, "deleted", Id: relationTypeId);
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT EXISTS(SELECT 1 FROM relation_types WHERE id=$id)";
        exists.Parameters.AddWithValue("$id", relationTypeId);
        return Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1
            ? new(false, "in_use", "relation_type has relations")
            : new(false, "not_found", "relation_type not found");
    }

    /// <summary>SQLiteオンラインバックアップを指定ファイルへ作成します。</summary>
    public async Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new ArgumentException("backup directory is required", nameof(destinationPath)));
        await using var source = await OpenAsync(cancellationToken);
        var targetString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        await using var target = new SqliteConnection(targetString);
        await target.OpenAsync(cancellationToken);
        source.BackupDatabase(target);
    }

    /// <summary>Entity を取得します。</summary>
    public async Task<EntityRecord?> GetEntityAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,canonical_name,class_name,namespace,metadata,created_at,updated_at FROM entities WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntity(reader) : null;
    }

    /// <summary>Entity を名前で検索します。</summary>
    public async Task<IReadOnlyList<EntityRecord>> SearchEntitiesAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,canonical_name,class_name,namespace,metadata,created_at,updated_at FROM entities WHERE canonical_name LIKE $query ESCAPE '\\' ORDER BY canonical_name LIMIT $limit";
        command.Parameters.AddWithValue("$query", $"%{EscapeLike(query)}%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<EntityRecord>();
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadEntity(reader));
        return results;
    }

    /// <summary>構造化Eventをactor、place、期間で検索します。</summary>
    public async Task<IReadOnlyList<EventSearchRecord>> QueryEventsAsync(string? actor = null, string? place = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string entityNamespace = "global", int limit = 50, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityNamespace);
        if (from is not null && to is not null && to <= from) throw new ArgumentException("to must be greater than from", nameof(to));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.entity_id,event_entity.canonical_name,e.actor_id,actor.canonical_name,e.occurred_at,e.ends_at,e.action,e.object_id,place.canonical_name,e.source_statement_id,statement.canonical_name
            FROM events e
            JOIN entities event_entity ON event_entity.id=e.entity_id
            LEFT JOIN entities actor ON actor.id=e.actor_id
            LEFT JOIN entities place ON place.id=e.object_id
            LEFT JOIN entities statement ON statement.id=e.source_statement_id
            WHERE event_entity.namespace=$namespace
              AND ($actor IS NULL OR actor.canonical_name LIKE $actor ESCAPE '\')
              AND ($place IS NULL OR place.canonical_name LIKE $place ESCAPE '\')
              AND ($from IS NULL OR COALESCE(e.ends_at,e.occurred_at)>=$from)
              AND ($to IS NULL OR e.occurred_at<$to)
            ORDER BY e.occurred_at,e.entity_id
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$namespace", entityNamespace);
        command.Parameters.AddWithValue("$actor", string.IsNullOrWhiteSpace(actor) ? DBNull.Value : $"%{EscapeLike(actor)}%");
        command.Parameters.AddWithValue("$place", string.IsNullOrWhiteSpace(place) ? DBNull.Value : $"%{EscapeLike(place)}%");
        command.Parameters.AddWithValue("$from", from is null ? DBNull.Value : Format(from.Value));
        command.Parameters.AddWithValue("$to", to is null ? DBNull.Value : Format(to.Value));
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<EventSearchRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return results;
    }

    /// <summary>Relation/Claim を条件検索します。該当なしは Open World の unknown を表します。</summary>
    public async Task<IReadOnlyList<ClaimRecord>> QueryClaimsAsync(long? entityId = null, string? relationType = null, DateTimeOffset? validAt = null, bool includeRetracted = false, bool includeStale = false, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = QuerySql + " WHERE ($entity IS NULL OR d.subject_id=$entity OR d.object_id=$entity OR s.entity_a_id=$entity OR s.entity_b_id=$entity) AND ($type IS NULL OR rt.canonical_name=$type) AND ($retracted=1 OR c.status<>'retracted') AND ($stale=1 OR c.status<>'stale') AND ($at IS NULL OR (c.valid_from IS NULL OR c.valid_from<=$at) AND (c.valid_to IS NULL OR c.valid_to>$at)) ORDER BY c.id";
        command.Parameters.AddWithValue("$entity", (object?)entityId ?? DBNull.Value);
        command.Parameters.AddWithValue("$type", (object?)relationType ?? DBNull.Value);
        command.Parameters.AddWithValue("$retracted", includeRetracted ? 1 : 0);
        command.Parameters.AddWithValue("$stale", includeStale ? 1 : 0);
        command.Parameters.AddWithValue("$at", validAt is null ? DBNull.Value : Format(validAt.Value));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<ClaimRecord>();
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadClaim(reader));
        return results;
    }

    /// <summary>期限を超えた現在性未確認の Claim を stale にします。</summary>
    public async Task<DreamResult> RunDreamAsync(CancellationToken cancellationToken = default)
    {
        var now = Now();
        await using var connection = await OpenAsync(cancellationToken);
        await ConfigureDreamTempStoreAsync(connection, cancellationToken);

        try
        {
            await CreateDreamTableAsync(connection, now, cancellationToken);
            var examined = await CountDreamRowsAsync(connection, cancellationToken);
            await _dreamExecutionHook.AfterStagingAsync(cancellationToken);

            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            await using var reducedStaleCommand = connection.CreateCommand();
            reducedStaleCommand.Transaction = transaction;
            reducedStaleCommand.CommandText = "SELECT COUNT(*) FROM dream_updates d JOIN claims c ON c.id=d.claim_id WHERE c.status='active' AND d.target_status='stale' AND d.target_confidence<c.claim_confidence AND d.expected_updated_at=c.updated_at";
            var reducedStale = Convert.ToInt32(await reducedStaleCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

            command.CommandText = """
                UPDATE claims
                SET claim_confidence = (SELECT d.target_confidence FROM dream_updates d WHERE d.claim_id = claims.id),
                    status = 'stale',
                    updated_at = (SELECT d.target_updated_at FROM dream_updates d WHERE d.claim_id = claims.id)
                WHERE status = 'active'
                  AND EXISTS (
                      SELECT 1
                      FROM dream_updates d
                      WHERE d.claim_id = claims.id
                        AND d.target_status = 'stale'
                        AND d.expected_updated_at = claims.updated_at
                  )
                """;
            var stale = await command.ExecuteNonQueryAsync(cancellationToken);

            command.CommandText = """
                UPDATE claims
                SET claim_confidence = (SELECT d.target_confidence FROM dream_updates d WHERE d.claim_id = claims.id),
                    updated_at = (SELECT d.target_updated_at FROM dream_updates d WHERE d.claim_id = claims.id)
                WHERE status = 'active'
                  AND EXISTS (
                      SELECT 1
                      FROM dream_updates d
                      WHERE d.claim_id = claims.id
                        AND d.target_status = 'active'
                        AND d.target_confidence < claims.claim_confidence
                        AND d.expected_updated_at = claims.updated_at
                  )
                """;
            var reducedActive = await command.ExecuteNonQueryAsync(cancellationToken);
            await _dreamExecutionHook.AfterUpdateAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(examined, stale, now) { ReducedConfidence = reducedStale + reducedActive };
        }
        finally
        {
            await DropDreamTableAsync(connection);
        }
    }

    private async Task ConfigureDreamTempStoreAsync(SqliteConnection connection, CancellationToken token)
    {
        if (_dreamTempStore == DreamTempStore.Default) return;
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA temp_store = {(_dreamTempStore == DreamTempStore.Memory ? "MEMORY" : "FILE")}";
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task CreateDreamTableAsync(SqliteConnection connection, DateTimeOffset now, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TEMP TABLE dream_updates (
                claim_id INTEGER PRIMARY KEY,
                expected_updated_at TEXT NOT NULL,
                target_status TEXT NOT NULL CHECK(target_status IN ('active', 'stale')),
                target_confidence REAL NOT NULL CHECK(target_confidence BETWEEN 0 AND 1),
                target_updated_at TEXT NOT NULL
            );

            INSERT INTO dream_updates(claim_id, expected_updated_at, target_status, target_confidence, target_updated_at)
            SELECT c.id,
                   c.updated_at,
                   CASE
                       WHEN rt.canonical_name = 'remembers'
                            AND unixepoch($now) - unixepoch(c.updated_at) > rt.refresh_after_seconds
                            AND c.claim_confidence * $decay < $stale_threshold
                           THEN 'stale'
                       WHEN unixepoch($now) - unixepoch(COALESCE(c.last_confirmed_at, c.observed_at)) > rt.refresh_after_seconds
                            AND rt.canonical_name <> 'remembers'
                           THEN 'stale'
                       ELSE 'active'
                   END,
                   CASE
                       WHEN rt.canonical_name = 'remembers'
                            AND unixepoch($now) - unixepoch(c.updated_at) > rt.refresh_after_seconds
                           THEN c.claim_confidence * $decay
                       ELSE c.claim_confidence
                   END,
                   $now
            FROM claims c
            JOIN relations r ON r.id = c.relation_id
            JOIN relation_types rt ON rt.id = r.relation_type_id
            WHERE c.status = 'active'
              AND rt.freshness_policy <> 'permanent'
              AND rt.refresh_after_seconds IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$now", Format(now));
        command.Parameters.AddWithValue("$decay", RememberDecayFactor);
        command.Parameters.AddWithValue("$stale_threshold", RememberStaleThreshold);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<int> CountDreamRowsAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dream_updates";
        return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    private static async Task DropDreamTableAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE IF EXISTS dream_updates";
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static void ValidateRememberedEvent(RememberedEventInput? input)
    {
        if (input is null) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Place);
        if (input.EndsAt <= input.StartsAt) throw new ArgumentException("event ends_at must be greater than starts_at", nameof(input));
    }

    private static async Task EnsureEventColumnsAsync(SqliteConnection connection, CancellationToken token)
    {
        await EnsureColumnAsync(connection, "ends_at", "ALTER TABLE events ADD COLUMN ends_at TEXT", token);
        await EnsureColumnAsync(connection, "source_statement_id", "ALTER TABLE events ADD COLUMN source_statement_id INTEGER REFERENCES entities(id)", token);
        await using var indexes = connection.CreateCommand();
        indexes.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_events_actor_time ON events(actor_id,occurred_at);
            CREATE INDEX IF NOT EXISTS idx_events_object_time ON events(object_id,occurred_at);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_events_source_statement ON events(source_statement_id) WHERE source_statement_id IS NOT NULL;
            """;
        await indexes.ExecuteNonQueryAsync(token);
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string columnName, string alterSql, CancellationToken token)
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT EXISTS(SELECT 1 FROM pragma_table_info('events') WHERE name=$name)";
        exists.Parameters.AddWithValue("$name", columnName);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == 1) return;
        await using var alter = connection.CreateCommand();
        alter.CommandText = alterSql;
        await alter.ExecuteNonQueryAsync(token);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken token) { var c = new SqliteConnection(_connectionString); await c.OpenAsync(token); return c; }
    private DateTimeOffset Now() => _timeProvider.GetUtcNow();
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    private static EntityRecord ReadEntity(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), DateTimeOffset.Parse(r.GetString(5), CultureInfo.InvariantCulture), DateTimeOffset.Parse(r.GetString(6), CultureInfo.InvariantCulture));
    private static ClaimRecord ReadClaim(SqliteDataReader r) => new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), Enum.Parse<RelationKind>(r.GetString(3), true), r.GetInt64(4), r.GetInt64(5), Enum.Parse<Polarity>(r.GetString(6), true), r.GetDouble(7), r.IsDBNull(8) ? null : r.GetDouble(8), r.IsDBNull(9) ? null : r.GetDouble(9), r.IsDBNull(10) ? null : r.GetInt64(10), r.IsDBNull(11) ? null : r.GetInt64(11), r.GetString(12), DateTimeOffset.Parse(r.GetString(13), CultureInfo.InvariantCulture), r.IsDBNull(14) ? null : DateTimeOffset.Parse(r.GetString(14), CultureInfo.InvariantCulture), r.IsDBNull(15) ? null : DateTimeOffset.Parse(r.GetString(15), CultureInfo.InvariantCulture), r.IsDBNull(16) ? null : DateTimeOffset.Parse(r.GetString(16), CultureInfo.InvariantCulture), Enum.Parse<ClaimStatus>(r.GetString(17), true));

    private static async Task<(long Id, bool Created)> GetOrCreateEntityAsync(SqliteConnection c, SqliteTransaction t, string name, string className, string entityNamespace, DateTimeOffset now, CancellationToken token)
    {
        await using var find = c.CreateCommand();
        find.Transaction = t;
        find.CommandText = "SELECT id FROM entities WHERE canonical_name=$name AND class_name=$class AND namespace=$namespace ORDER BY id LIMIT 1";
        find.Parameters.AddWithValue("$name", name);
        find.Parameters.AddWithValue("$class", className);
        find.Parameters.AddWithValue("$namespace", entityNamespace);
        var existing = await find.ExecuteScalarAsync(token);
        if (existing is long id) return (id, false);

        await using var insert = c.CreateCommand();
        insert.Transaction = t;
        insert.CommandText = "INSERT INTO entities(class_name,canonical_name,namespace,created_at,updated_at) VALUES($class,$name,$namespace,$now,$now); SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$class", className);
        insert.Parameters.AddWithValue("$name", name);
        insert.Parameters.AddWithValue("$namespace", entityNamespace);
        insert.Parameters.AddWithValue("$now", Format(now));
        return ((long)(await insert.ExecuteScalarAsync(token) ?? 0L), true);
    }

    private static async Task<long?> PersistRememberedEventAsync(SqliteConnection connection, SqliteTransaction transaction, long statementId, string statementText, string entityNamespace, RememberedEventInput? input, DateTimeOffset now, CancellationToken token)
    {
        if (input is null) return null;
        await using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT entity_id FROM events WHERE source_statement_id=$statement";
        find.Parameters.AddWithValue("$statement", statementId);
        var existing = await find.ExecuteScalarAsync(token);
        if (existing is long existingId) return existingId;

        var (actorId, _) = await GetOrCreateEntityAsync(connection, transaction, input.Actor.Trim(), "Actor", entityNamespace, now, token);
        var (placeId, _) = await GetOrCreateEntityAsync(connection, transaction, input.Place.Trim(), "Place", entityNamespace, now, token);
        var canonicalName = string.IsNullOrWhiteSpace(input.CanonicalName) ? statementText : input.CanonicalName.Trim();
        var (eventId, _) = await GetOrCreateEntityAsync(connection, transaction, canonicalName, "Event", entityNamespace, now, token);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO events(entity_id,actor_id,occurred_at,action,object_id,ends_at,source_statement_id) VALUES($entity,$actor,$starts,$action,$place,$ends,$statement)";
        insert.Parameters.AddWithValue("$entity", eventId);
        insert.Parameters.AddWithValue("$actor", actorId);
        insert.Parameters.AddWithValue("$starts", Format(input.StartsAt));
        insert.Parameters.AddWithValue("$action", input.Action.Trim());
        insert.Parameters.AddWithValue("$place", placeId);
        insert.Parameters.AddWithValue("$ends", Format(input.EndsAt));
        insert.Parameters.AddWithValue("$statement", statementId);
        await insert.ExecuteNonQueryAsync(token);
        return eventId;
    }

    private static async Task<(long Id, bool Created)> GetOrCreateRememberRelationTypeAsync(SqliteConnection c, SqliteTransaction t, DateTimeOffset now, CancellationToken token)
    {
        await using var find = c.CreateCommand();
        find.Transaction = t;
        find.CommandText = "SELECT id FROM relation_types WHERE canonical_name='remembers'";
        var existing = await find.ExecuteScalarAsync(token);
        if (existing is long id) return (id, false);

        await using var insert = c.CreateCommand();
        insert.Transaction = t;
        insert.CommandText = "INSERT INTO relation_types(canonical_name,category,directionality,allow_strength,freshness_policy,refresh_after_seconds,description,created_at,updated_at) VALUES('remembers','memory','directed',0,'periodic',$refresh,$description,$now,$now); SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$refresh", RememberRefreshAfterSeconds);
        insert.Parameters.AddWithValue("$description", "A conversation user explicitly requested that a textual fact be retained.");
        insert.Parameters.AddWithValue("$now", Format(now));
        return ((long)(await insert.ExecuteScalarAsync(token) ?? 0L), true);
    }

    private static async Task<long?> FindRememberedClaimAsync(SqliteConnection c, SqliteTransaction t, long relationId, CancellationToken token)
    {
        await using var find = c.CreateCommand();
        find.Transaction = t;
        find.CommandText = "SELECT id FROM claims WHERE relation_id=$relation AND assertion_type='remembered_text' AND polarity='positive' AND status<>'retracted' ORDER BY id LIMIT 1";
        find.Parameters.AddWithValue("$relation", relationId);
        var existing = await find.ExecuteScalarAsync(token);
        return existing is long id ? id : null;
    }

    private static async Task ReconfirmRememberedClaimAsync(SqliteConnection c, SqliteTransaction t, long claimId, double confidence, DateTimeOffset now, CancellationToken token)
    {
        await using var update = c.CreateCommand();
        update.Transaction = t;
        update.CommandText = "UPDATE claims SET claim_confidence=MAX(claim_confidence,$confidence),status='active',last_confirmed_at=$now,updated_at=$now WHERE id=$id";
        update.Parameters.AddWithValue("$id", claimId);
        update.Parameters.AddWithValue("$confidence", confidence);
        update.Parameters.AddWithValue("$now", Format(now));
        await update.ExecuteNonQueryAsync(token);
    }

    private static async Task<(long Id, RelationKind Kind, bool AllowStrength)?> GetRelationTypeAsync(SqliteConnection c, SqliteTransaction t, string name, CancellationToken token) { await using var q = c.CreateCommand(); q.Transaction = t; q.CommandText = "SELECT id,directionality,allow_strength FROM relation_types WHERE canonical_name=$name OR id IN(SELECT relation_type_id FROM relation_type_aliases WHERE alias=$name)"; q.Parameters.AddWithValue("$name", name); await using var r = await q.ExecuteReaderAsync(token); return await r.ReadAsync(token) ? (r.GetInt64(0), Enum.Parse<RelationKind>(r.GetString(1), true), r.GetBoolean(2)) : null; }
    private static async Task<bool> EntitiesExistAsync(SqliteConnection c, SqliteTransaction t, ClaimCandidate x, CancellationToken token) { await using var q = c.CreateCommand(); q.Transaction = t; q.CommandText = "SELECT COUNT(*) FROM entities WHERE id IN($s,$o,$k)"; q.Parameters.AddWithValue("$s", x.SubjectId); q.Parameters.AddWithValue("$o", x.ObjectId); q.Parameters.AddWithValue("$k", x.KnowledgeSubjectId ?? x.SubjectId); var expected = x.KnowledgeSubjectId is null || x.KnowledgeSubjectId == x.SubjectId || x.KnowledgeSubjectId == x.ObjectId ? 2 : 3; return Convert.ToInt32(await q.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == expected; }
    private async Task<long> GetOrCreateRelationAsync(SqliteConnection c, SqliteTransaction t, long typeId, RelationKind kind, long subject, long obj, CancellationToken token) { var a = kind == RelationKind.Symmetric ? Math.Min(subject, obj) : subject; var b = kind == RelationKind.Symmetric ? Math.Max(subject, obj) : obj; await using var find = c.CreateCommand(); find.Transaction = t; find.CommandText = kind == RelationKind.Directed ? "SELECT r.id FROM relations r JOIN directed_relations d ON d.relation_id=r.id WHERE r.relation_type_id=$type AND d.subject_id=$a AND d.object_id=$b" : "SELECT r.id FROM relations r JOIN symmetric_relations s ON s.relation_id=r.id WHERE r.relation_type_id=$type AND s.entity_a_id=$a AND s.entity_b_id=$b"; find.Parameters.AddWithValue("$type", typeId); find.Parameters.AddWithValue("$a", a); find.Parameters.AddWithValue("$b", b); var existing = await find.ExecuteScalarAsync(token); if (existing is long id) return id; await using var insert = c.CreateCommand(); insert.Transaction = t; insert.CommandText = "INSERT INTO relations(relation_type_id,relation_kind,created_at) VALUES($type,$kind,$now); SELECT last_insert_rowid();"; insert.Parameters.AddWithValue("$type", typeId); insert.Parameters.AddWithValue("$kind", Lower(kind)); insert.Parameters.AddWithValue("$now", Format(Now())); var relationId = (long)(await insert.ExecuteScalarAsync(token) ?? 0L); await using var edge = c.CreateCommand(); edge.Transaction = t; edge.CommandText = kind == RelationKind.Directed ? "INSERT INTO directed_relations VALUES($id,$a,$b)" : "INSERT INTO symmetric_relations VALUES($id,$a,$b)"; edge.Parameters.AddWithValue("$id", relationId); edge.Parameters.AddWithValue("$a", a); edge.Parameters.AddWithValue("$b", b); await edge.ExecuteNonQueryAsync(token); return relationId; }
    private async Task<long> InsertSourceAsync(SqliteConnection c, SqliteTransaction t, SourceInput x, CancellationToken token) { await using var q = c.CreateCommand(); q.Transaction = t; q.CommandText = "INSERT INTO sources(source_type,uri,external_id,title,author_entity_id,source_reliability,observed_at,metadata) VALUES($type,$uri,$external,$title,$author,$reliability,$now,$metadata); SELECT last_insert_rowid();"; q.Parameters.AddWithValue("$type", x.SourceType); q.Parameters.AddWithValue("$uri", (object?)x.Uri ?? DBNull.Value); q.Parameters.AddWithValue("$external", (object?)x.ExternalId ?? DBNull.Value); q.Parameters.AddWithValue("$title", (object?)x.Title ?? DBNull.Value); q.Parameters.AddWithValue("$author", (object?)x.AuthorEntityId ?? DBNull.Value); q.Parameters.AddWithValue("$reliability", (object?)x.Reliability ?? DBNull.Value); q.Parameters.AddWithValue("$now", Format(Now())); q.Parameters.AddWithValue("$metadata", (object?)x.Metadata ?? DBNull.Value); return (long)(await q.ExecuteScalarAsync(token) ?? 0L); }
    private async Task<long> InsertClaimAsync(SqliteConnection c, SqliteTransaction t, long relationId, long? sourceId, ClaimCandidate x, CancellationToken token) { var now = Now(); await using var q = c.CreateCommand(); q.Transaction = t; q.CommandText = "INSERT INTO claims(relation_id,knowledge_subject_id,polarity,claim_confidence,attribution_confidence,strength,assertion_type,source_id,observed_at,valid_from,valid_to,last_confirmed_at,status,created_at,updated_at) VALUES($relation,$knowledge,$polarity,$confidence,$attribution,$strength,$assertion,$source,$observed,$from,$to,$confirmed,'active',$now,$now); SELECT last_insert_rowid();"; q.Parameters.AddWithValue("$relation", relationId); q.Parameters.AddWithValue("$knowledge", (object?)x.KnowledgeSubjectId ?? DBNull.Value); q.Parameters.AddWithValue("$polarity", Lower(x.Polarity)); q.Parameters.AddWithValue("$confidence", x.Confidence); q.Parameters.AddWithValue("$attribution", (object?)x.AttributionConfidence ?? DBNull.Value); q.Parameters.AddWithValue("$strength", (object?)x.Strength ?? DBNull.Value); q.Parameters.AddWithValue("$assertion", x.AssertionType); q.Parameters.AddWithValue("$source", (object?)sourceId ?? DBNull.Value); q.Parameters.AddWithValue("$observed", Format(x.ObservedAt ?? now)); q.Parameters.AddWithValue("$from", x.ValidFrom is null ? DBNull.Value : Format(x.ValidFrom.Value)); q.Parameters.AddWithValue("$to", x.ValidTo is null ? DBNull.Value : Format(x.ValidTo.Value)); q.Parameters.AddWithValue("$confirmed", x.LastConfirmedAt is null ? DBNull.Value : Format(x.LastConfirmedAt.Value)); q.Parameters.AddWithValue("$now", Format(now)); return (long)(await q.ExecuteScalarAsync(token) ?? 0L); }

    private const string QuerySql = "SELECT c.id,r.id,rt.canonical_name,r.relation_kind,COALESCE(d.subject_id,s.entity_a_id),COALESCE(d.object_id,s.entity_b_id),c.polarity,c.claim_confidence,c.attribution_confidence,c.strength,c.knowledge_subject_id,c.source_id,c.assertion_type,c.observed_at,c.valid_from,c.valid_to,c.last_confirmed_at,c.status FROM claims c JOIN relations r ON r.id=c.relation_id JOIN relation_types rt ON rt.id=r.relation_type_id LEFT JOIN directed_relations d ON d.relation_id=r.id LEFT JOIN symmetric_relations s ON s.relation_id=r.id";
    private const string Schema = """
PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS entities(id INTEGER PRIMARY KEY,class_name TEXT NOT NULL,canonical_name TEXT NOT NULL,namespace TEXT NOT NULL DEFAULT 'global',metadata TEXT,created_at TEXT NOT NULL,updated_at TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS idx_entities_name ON entities(canonical_name);
CREATE TABLE IF NOT EXISTS relation_types(id INTEGER PRIMARY KEY,canonical_name TEXT NOT NULL UNIQUE,category TEXT NOT NULL,directionality TEXT NOT NULL CHECK(directionality IN('directed','symmetric')),allow_strength INTEGER NOT NULL DEFAULT 0,inverse_name TEXT,freshness_policy TEXT NOT NULL CHECK(freshness_policy IN('permanent','periodic','volatile')),refresh_after_seconds INTEGER,description TEXT,created_at TEXT NOT NULL,updated_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS relation_type_aliases(relation_type_id INTEGER NOT NULL REFERENCES relation_types(id),alias TEXT NOT NULL UNIQUE,PRIMARY KEY(relation_type_id,alias));
CREATE TABLE IF NOT EXISTS relations(id INTEGER PRIMARY KEY,relation_type_id INTEGER NOT NULL REFERENCES relation_types(id),relation_kind TEXT NOT NULL CHECK(relation_kind IN('directed','symmetric')),created_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS directed_relations(relation_id INTEGER PRIMARY KEY REFERENCES relations(id),subject_id INTEGER NOT NULL REFERENCES entities(id),object_id INTEGER NOT NULL REFERENCES entities(id),UNIQUE(subject_id,object_id,relation_id));
CREATE INDEX IF NOT EXISTS idx_directed_subject ON directed_relations(subject_id); CREATE INDEX IF NOT EXISTS idx_directed_object ON directed_relations(object_id);
CREATE TABLE IF NOT EXISTS symmetric_relations(relation_id INTEGER PRIMARY KEY REFERENCES relations(id),entity_a_id INTEGER NOT NULL REFERENCES entities(id),entity_b_id INTEGER NOT NULL REFERENCES entities(id),CHECK(entity_a_id<entity_b_id),UNIQUE(entity_a_id,entity_b_id,relation_id));
CREATE INDEX IF NOT EXISTS idx_symmetric_a ON symmetric_relations(entity_a_id); CREATE INDEX IF NOT EXISTS idx_symmetric_b ON symmetric_relations(entity_b_id);
CREATE TABLE IF NOT EXISTS sources(id INTEGER PRIMARY KEY,source_type TEXT NOT NULL,uri TEXT,external_id TEXT,title TEXT,author_entity_id INTEGER REFERENCES entities(id),source_reliability REAL CHECK(source_reliability BETWEEN 0 AND 1),observed_at TEXT NOT NULL,metadata TEXT);
CREATE TABLE IF NOT EXISTS claims(id INTEGER PRIMARY KEY,relation_id INTEGER NOT NULL REFERENCES relations(id),knowledge_subject_id INTEGER REFERENCES entities(id),polarity TEXT NOT NULL CHECK(polarity IN('positive','negative')),claim_confidence REAL NOT NULL CHECK(claim_confidence BETWEEN 0 AND 1),attribution_confidence REAL CHECK(attribution_confidence BETWEEN 0 AND 1),strength REAL CHECK(strength BETWEEN 0 AND 1),assertion_type TEXT NOT NULL,source_id INTEGER REFERENCES sources(id),observed_at TEXT NOT NULL,valid_from TEXT,valid_to TEXT,last_confirmed_at TEXT,status TEXT NOT NULL CHECK(status IN('active','retracted','stale')),created_at TEXT NOT NULL,updated_at TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS idx_claim_relation ON claims(relation_id); CREATE INDEX IF NOT EXISTS idx_claim_subject ON claims(knowledge_subject_id); CREATE INDEX IF NOT EXISTS idx_claim_temporal ON claims(valid_from,valid_to);
CREATE TABLE IF NOT EXISTS events(entity_id INTEGER PRIMARY KEY REFERENCES entities(id),actor_id INTEGER REFERENCES entities(id),occurred_at TEXT NOT NULL,action TEXT NOT NULL,object_id INTEGER REFERENCES entities(id),object_value TEXT,ends_at TEXT,source_statement_id INTEGER REFERENCES entities(id));
""";
}
