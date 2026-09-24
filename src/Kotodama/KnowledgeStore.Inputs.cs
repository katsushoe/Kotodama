using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Kotodama;

public sealed partial class KnowledgeStore
{
    internal const int MaximumEntityNameLength = 80;
    internal const int MaximumEntityNameWords = 8;

    private static readonly string[] SentenceEndings =
    [
        "です", "ます", "でした", "ました", "である", "だった", "となる", "している", "される", "できる"
    ];

    private static readonly string[] DescriptivePhraseMarkers =
    [
        "での", "による", "により", "における", "として", "ための", "後の", "前の", "時の", "し、", "し，"
    ];

    private async Task<bool> MigrateLegacyStatementsAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='statements')";
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == 0)
        {
            await SetSchemaVersionAsync(connection, token);
            return false;
        }

        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys=OFF";
        await foreignKeys.ExecuteNonQueryAsync(token);
        try
        {
            await using var transaction = connection.BeginTransaction(deferred: false);
            var rows = await ReadLegacyStatementsAsync(connection, transaction, token);
            foreach (var row in rows)
            {
                await using var input = connection.CreateCommand();
                input.Transaction = transaction;
                input.CommandText = "INSERT OR IGNORE INTO knowledge_inputs(id,namespace,created_at,updated_at) VALUES($id,$namespace,$created,$updated)";
                input.Parameters.AddWithValue("$id", row.Id);
                input.Parameters.AddWithValue("$namespace", row.Namespace);
                input.Parameters.AddWithValue("$created", row.CreatedAt);
                input.Parameters.AddWithValue("$updated", row.UpdatedAt);
                await input.ExecuteNonQueryAsync(token);
                IReadOnlyList<ExtractedTerm> terms;
                try { terms = _termExtractor.Extract(row.Text); }
                catch (ArgumentException) { terms = []; }
                foreach (var term in terms)
                {
                    if (GetAtomicEntityNameError(term.CanonicalName, "Term") is not null) continue;
                    var (entityId, _) = await GetOrCreateEntityAsync(connection, transaction, term.CanonicalName, "Term", row.Namespace, Now(), token);
                    await InsertInputTermAsync(connection, transaction, row.Id, entityId, term.OccurrenceCount, token);
                }
            }
            rows.Clear();

            await RebuildLegacyInputReferencesAsync(connection, transaction, token);
            await RemoveLegacyStatementGraphAsync(connection, transaction, token);
            await SetSchemaVersionAsync(connection, transaction, token);
            await transaction.CommitAsync(token);
        }
        finally
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys=ON";
            await foreignKeys.ExecuteNonQueryAsync(token);
        }

        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await check.ExecuteReaderAsync(token);
        if (await reader.ReadAsync(token)) throw new InvalidOperationException("Legacy text migration failed foreign key validation.");
        return true;
    }

    private static async Task<List<LegacyStatement>> ReadLegacyStatementsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,text,namespace,created_at,updated_at FROM statements ORDER BY id";
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<LegacyStatement>();
        while (await reader.ReadAsync(token))
            rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    private static async Task RebuildLegacyInputReferencesAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS input_tags(
                input_id INTEGER NOT NULL REFERENCES knowledge_inputs(id) ON DELETE CASCADE,
                tag_id INTEGER NOT NULL REFERENCES tags(id), origin TEXT NOT NULL CHECK(origin IN('remember','manual')),
                PRIMARY KEY(input_id,tag_id,origin));
            """;
        await command.ExecuteNonQueryAsync(token);
        if (await HasLegacyColumnAsync(connection, transaction, "statement_tags", "statement_id", token))
        {
            command.CommandText = "INSERT OR IGNORE INTO input_tags(input_id,tag_id,origin) SELECT statement_id,tag_id,origin FROM statement_tags";
            await command.ExecuteNonQueryAsync(token);
        }

        if (await HasLegacyColumnAsync(connection, transaction, "sources", "source_statement_id", token))
        {
            command.CommandText = """
            CREATE TABLE sources_v2(id INTEGER PRIMARY KEY,source_type TEXT NOT NULL,uri TEXT,external_id TEXT,title TEXT,
                author_entity_id INTEGER REFERENCES entities(id),source_reliability REAL CHECK(source_reliability BETWEEN 0 AND 1),
                observed_at TEXT NOT NULL,metadata TEXT,source_input_id INTEGER REFERENCES knowledge_inputs(id));
            INSERT INTO sources_v2 SELECT id,source_type,uri,external_id,title,author_entity_id,source_reliability,observed_at,metadata,source_statement_id FROM sources;
            DROP TABLE sources;
            ALTER TABLE sources_v2 RENAME TO sources;
            """;
            await command.ExecuteNonQueryAsync(token);
        }

        if (await HasLegacyColumnAsync(connection, transaction, "events", "source_statement_id", token))
        {
            command.CommandText = """
            CREATE TABLE events_v2(entity_id INTEGER PRIMARY KEY REFERENCES entities(id),actor_id INTEGER REFERENCES entities(id),
                occurred_at TEXT NOT NULL,action TEXT NOT NULL,object_id INTEGER REFERENCES entities(id),object_value TEXT,ends_at TEXT,
                source_input_id INTEGER REFERENCES knowledge_inputs(id));
            INSERT INTO events_v2 SELECT entity_id,actor_id,occurred_at,action,object_id,object_value,ends_at,source_statement_id FROM events;
            DROP TABLE events;
            ALTER TABLE events_v2 RENAME TO events;
            """;
            await command.ExecuteNonQueryAsync(token);
        }

        if (await HasLegacyColumnAsync(connection, transaction, "claim_tags", "source_statement_id", token))
        {
            command.CommandText = """
            CREATE TABLE claim_tags_v2(claim_id INTEGER NOT NULL REFERENCES claims(id) ON DELETE CASCADE,
                tag_id INTEGER NOT NULL REFERENCES tags(id),origin TEXT NOT NULL CHECK(origin IN('inherited','manual')),
                source_input_id INTEGER REFERENCES knowledge_inputs(id),PRIMARY KEY(claim_id,tag_id,origin));
            INSERT INTO claim_tags_v2 SELECT claim_id,tag_id,origin,source_statement_id FROM claim_tags;
            DROP TABLE claim_tags;
            ALTER TABLE claim_tags_v2 RENAME TO claim_tags;
            """;
            await command.ExecuteNonQueryAsync(token);
        }
        else
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS claim_tags(
                    claim_id INTEGER NOT NULL REFERENCES claims(id) ON DELETE CASCADE,
                    tag_id INTEGER NOT NULL REFERENCES tags(id),origin TEXT NOT NULL CHECK(origin IN('inherited','manual')),
                    source_input_id INTEGER REFERENCES knowledge_inputs(id),PRIMARY KEY(claim_id,tag_id,origin));
                """;
            await command.ExecuteNonQueryAsync(token);
        }
    }

    private static async Task<bool> HasLegacyColumnAsync(SqliteConnection connection, SqliteTransaction transaction,
        string table, string column, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM pragma_table_info($table) WHERE name=$column)";
        command.Parameters.AddWithValue("$table", table);
        command.Parameters.AddWithValue("$column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0;
    }

    private static async Task RemoveLegacyStatementGraphAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM claim_tags WHERE claim_id IN(SELECT id FROM claims WHERE assertion_type='remembered_text');
            DELETE FROM claims WHERE assertion_type='remembered_text';
            DELETE FROM claims WHERE relation_id IN(
                SELECT d.relation_id FROM directed_relations d
                JOIN entities a ON a.id=d.subject_id JOIN entities b ON b.id=d.object_id
                WHERE a.class_name IN('Statement','StatementRef') OR b.class_name IN('Statement','StatementRef'));
            DELETE FROM directed_relations WHERE subject_id IN(SELECT id FROM entities WHERE class_name IN('Statement','StatementRef'))
                OR object_id IN(SELECT id FROM entities WHERE class_name IN('Statement','StatementRef'));
            DELETE FROM relations WHERE id NOT IN(SELECT relation_id FROM directed_relations UNION SELECT relation_id FROM symmetric_relations);
            DELETE FROM relation_types WHERE canonical_name='remembers' AND id NOT IN(SELECT relation_type_id FROM relations);
            DELETE FROM sources WHERE id NOT IN(SELECT source_id FROM claims WHERE source_id IS NOT NULL);
            DELETE FROM entities WHERE class_name IN('Statement','StatementRef');
            DROP TABLE IF EXISTS statement_tags;
            DROP TABLE statements;
            """;
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task SetSchemaVersionAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO schema_metadata(key,value) VALUES('protocol_version','2') ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task SetSchemaVersionAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO schema_metadata(key,value) VALUES('protocol_version','2') ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task SecureCompactAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "PRAGMA journal_mode=DELETE";
        await command.ExecuteScalarAsync(token);
        command.CommandText = "VACUUM";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = "PRAGMA journal_mode=WAL";
        await command.ExecuteScalarAsync(token);
    }

    private static async Task<bool> IsSecureCompactionCompleteAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM schema_metadata WHERE key='secure_compaction_completed'";
        return string.Equals(await command.ExecuteScalarAsync(token) as string, "1", StringComparison.Ordinal);
    }

    private static async Task MarkSecureCompactionCompleteAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO schema_metadata(key,value) VALUES('secure_compaction_completed','1') ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        await command.ExecuteNonQueryAsync(token);
    }

    private sealed record LegacyStatement(long Id, string Text, string Namespace, string CreatedAt, string UpdatedAt);

    private static async Task<long> CreateKnowledgeInputAsync(SqliteConnection connection, SqliteTransaction transaction,
        string entityNamespace, DateTimeOffset now, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO knowledge_inputs(namespace,created_at,updated_at) VALUES($namespace,$now,$now); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$namespace", entityNamespace);
        command.Parameters.AddWithValue("$now", Format(now));
        return (long)(await command.ExecuteScalarAsync(token) ?? 0L);
    }

    private async Task<int> PersistInputTermsAsync(SqliteConnection connection, SqliteTransaction transaction, long inputId,
        string text, string entityNamespace, IReadOnlyDictionary<string, long> explicitEntities, DateTimeOffset now, CancellationToken token)
    {
        var count = 0;
        foreach (var term in _termExtractor.Extract(text))
        {
            var existing = explicitEntities.FirstOrDefault(x => string.Equals(x.Key, term.CanonicalName, StringComparison.OrdinalIgnoreCase));
            long entityId;
            if (existing.Value != 0) entityId = existing.Value;
            else
            {
                if (GetAtomicEntityNameError(term.CanonicalName, "Term") is not null) continue;
                (entityId, _) = await GetOrCreateEntityAsync(connection, transaction, term.CanonicalName, "Term", entityNamespace, now, token);
            }
            await InsertInputTermAsync(connection, transaction, inputId, entityId, term.OccurrenceCount, token);
            count++;
        }
        return count;
    }

    private static async Task InsertInputTermAsync(SqliteConnection connection, SqliteTransaction transaction, long inputId,
        long entityId, int occurrenceCount, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO input_terms(input_id,term_entity_id,occurrence_count) VALUES($input,$entity,$count) ON CONFLICT(input_id,term_entity_id) DO UPDATE SET occurrence_count=MAX(input_terms.occurrence_count,excluded.occurrence_count)";
        command.Parameters.AddWithValue("$input", inputId);
        command.Parameters.AddWithValue("$entity", entityId);
        command.Parameters.AddWithValue("$count", occurrenceCount);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<KnowledgeInputRecord?> ReadKnowledgeInputAsync(SqliteConnection connection, SqliteTransaction? transaction,
        long id, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,namespace,created_at,updated_at FROM knowledge_inputs WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var record = new KnowledgeInputRecord(reader.GetInt64(0), reader.GetString(1),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture), []);
        await reader.DisposeAsync();

        await using var terms = connection.CreateCommand();
        terms.Transaction = transaction;
        terms.CommandText = "SELECT e.id,e.canonical_name,t.occurrence_count FROM input_terms t JOIN entities e ON e.id=t.term_entity_id WHERE t.input_id=$id ORDER BY e.canonical_name,e.id";
        terms.Parameters.AddWithValue("$id", id);
        await using var termReader = await terms.ExecuteReaderAsync(token);
        var values = new List<InputTermRecord>();
        while (await termReader.ReadAsync(token)) values.Add(new(termReader.GetInt64(0), termReader.GetString(1), termReader.GetInt32(2)));
        return record with { Terms = values };
    }

    private static void ValidateAtomicEntityName(string canonicalName, string className, string? sourceText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        var name = canonicalName.Trim();
        if (sourceText is not null && string.Equals(name, sourceText.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Entity canonicalName must not contain the complete input text. Split it into atomic terms.");
        var error = GetAtomicEntityNameError(name, className);
        if (error is not null) throw new ArgumentException(error);
    }

    private static string? GetAtomicEntityNameError(string name, string className)
    {
        if (className is "Statement" or "StatementRef")
            return "Statement and StatementRef are not supported. Use atomic entities and knowledge inputs.";
        if (name.Length > MaximumEntityNameLength)
            return $"Entity canonicalName must be at most {MaximumEntityNameLength} characters.";
        if (name.IndexOfAny(['\r', '\n']) >= 0 || "。！？!?".Contains(name[^1]))
            return "Entity canonicalName must be an atomic term, not a sentence.";
        if (name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > MaximumEntityNameWords)
            return $"Entity canonicalName must contain at most {MaximumEntityNameWords} words.";
        if (SentenceEndings.Any(x => name.EndsWith(x, StringComparison.Ordinal)) ||
            DescriptivePhraseMarkers.Any(x => name.Contains(x, StringComparison.Ordinal)))
            return "Entity canonicalName must be an atomic term, not a sentence or descriptive phrase.";
        return null;
    }

    private static void ValidateSourceInput(SourceInput? input, string? rawInput = null)
    {
        if (input is null) return;
        if (rawInput is not null && new[] { input.Uri, input.ExternalId, input.Title, input.Metadata }
            .Any(value => string.Equals(value?.Trim(), rawInput.Trim(), StringComparison.Ordinal)))
            throw new ArgumentException("Source metadata must not contain the complete input text.", nameof(input));
        ValidateAtomicEntityName(input.SourceType, "SourceType");
        if (input.Reliability is double reliability && (!double.IsFinite(reliability) || reliability is < 0 or > 1))
            throw new ArgumentOutOfRangeException(nameof(input), "source reliability must be between 0 and 1");
        if (input.Uri is not null && (!Uri.TryCreate(input.Uri, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)))
            throw new ArgumentException("Source URI must be absolute and must not contain user information, query, or fragment.", nameof(input));
        ValidateOptionalAtomicMetadata(input.ExternalId, "source externalId");
        ValidateOptionalAtomicMetadata(input.Title, "source title");
        if (input.Metadata is null) return;
        try
        {
            using var document = JsonDocument.Parse(input.Metadata);
            ValidateMetadataElement(document.RootElement);
        }
        catch (JsonException error)
        {
            throw new ArgumentException("Source metadata must be a JSON object containing only non-text scalar metadata.", nameof(input), error);
        }
    }

    private static string? NormalizeSourceUri(string? value)
    {
        if (value is null) return null;
        var uri = new Uri(value, UriKind.Absolute);
        if (uri.Scheme is "http" or "https") return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        ValidateOptionalAtomicMetadata(uri.OriginalString, "source URI");
        return uri.OriginalString;
    }

    private static void ValidateMetadataElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Source metadata must be a JSON object containing only non-text scalar metadata.");
        foreach (var property in element.EnumerateObject())
        {
            ValidateOptionalAtomicMetadata(property.Name, "source metadata key");
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                throw new ArgumentException("Nested source metadata is not supported.");
            if (property.Value.ValueKind == JsonValueKind.String)
                ValidateOptionalAtomicMetadata(property.Value.GetString(), "source metadata value");
        }
    }

    private static void ValidateOptionalAtomicMetadata(string? value, string field)
    {
        if (value is null) return;
        if (value.Length is 0 or > MaximumEntityNameLength || GetAtomicEntityNameError(value, "Metadata") is not null)
            throw new ArgumentException($"{field} must be an atomic identifier or short noun phrase.");
    }
}
