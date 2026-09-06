using System.Globalization;
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

    private async Task InitializeStatementsAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO statements(id,text,namespace,created_at,updated_at)
            SELECT id,canonical_name,namespace,created_at,updated_at
            FROM entities WHERE class_name='Statement';

            UPDATE entities
            SET class_name='StatementRef', canonical_name='statement:' || id, updated_at=$now
            WHERE class_name='Statement';

            UPDATE entities
            SET canonical_name='event:' || id, updated_at=$now
            WHERE class_name='Event'
              AND EXISTS(
                  SELECT 1 FROM events e JOIN statements s ON s.id=e.source_statement_id
                  WHERE e.entity_id=entities.id AND s.text=entities.canonical_name);
            """;
        command.Parameters.AddWithValue("$now", Format(Now()));
        await command.ExecuteNonQueryAsync(token);
        await MigrateDescriptiveEntitiesAsync(connection, transaction, token);
        await transaction.CommitAsync(token);
    }

    private async Task MigrateDescriptiveEntitiesAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT id,canonical_name,class_name,namespace,created_at,updated_at
            FROM entities
            WHERE class_name<>'StatementRef'
              AND NOT EXISTS(
                  SELECT 1 FROM events
                  WHERE entity_id=entities.id OR actor_id=entities.id OR object_id=entities.id)
            ORDER BY id
            """;
        await using var reader = await select.ExecuteReaderAsync(token);
        var candidates = new List<(long Id, string Text, string Namespace, string CreatedAt, string UpdatedAt)>();
        while (await reader.ReadAsync(token))
        {
            var text = reader.GetString(1);
            if (GetAtomicEntityNameError(text, reader.GetString(2)) is not null)
                candidates.Add((reader.GetInt64(0), text, reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }
        await reader.DisposeAsync();

        foreach (var candidate in candidates)
        {
            await using var migrate = connection.CreateCommand();
            migrate.Transaction = transaction;
            migrate.CommandText = """
                INSERT OR IGNORE INTO statements(id,text,namespace,created_at,updated_at)
                VALUES($id,$text,$namespace,$created,$updated);
                UPDATE entities
                SET class_name='StatementRef', canonical_name='statement:' || id, updated_at=$now
                WHERE id=$id;
                """;
            migrate.Parameters.AddWithValue("$id", candidate.Id);
            migrate.Parameters.AddWithValue("$text", candidate.Text);
            migrate.Parameters.AddWithValue("$namespace", candidate.Namespace);
            migrate.Parameters.AddWithValue("$created", candidate.CreatedAt);
            migrate.Parameters.AddWithValue("$updated", candidate.UpdatedAt);
            migrate.Parameters.AddWithValue("$now", Format(Now()));
            await migrate.ExecuteNonQueryAsync(token);
        }
    }

    private static async Task<(long Id, bool Created)> GetOrCreateStatementAsync(SqliteConnection connection, SqliteTransaction transaction,
        string text, string entityNamespace, DateTimeOffset now, CancellationToken token)
    {
        await using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT id FROM statements WHERE text=$text AND namespace=$namespace ORDER BY id LIMIT 1";
        find.Parameters.AddWithValue("$text", text);
        find.Parameters.AddWithValue("$namespace", entityNamespace);
        if (await find.ExecuteScalarAsync(token) is long existing) return (existing, false);

        await using var entity = connection.CreateCommand();
        entity.Transaction = transaction;
        entity.CommandText = "INSERT INTO entities(class_name,canonical_name,namespace,created_at,updated_at) VALUES('StatementRef','statement:pending',$namespace,$now,$now); SELECT last_insert_rowid();";
        entity.Parameters.AddWithValue("$namespace", entityNamespace);
        entity.Parameters.AddWithValue("$now", Format(now));
        var id = (long)(await entity.ExecuteScalarAsync(token) ?? 0L);

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE entities SET canonical_name=$name WHERE id=$id";
        update.Parameters.AddWithValue("$name", $"statement:{id.ToString(CultureInfo.InvariantCulture)}");
        update.Parameters.AddWithValue("$id", id);
        await update.ExecuteNonQueryAsync(token);

        await using var statement = connection.CreateCommand();
        statement.Transaction = transaction;
        statement.CommandText = "INSERT INTO statements(id,text,namespace,created_at,updated_at) VALUES($id,$text,$namespace,$now,$now)";
        statement.Parameters.AddWithValue("$id", id);
        statement.Parameters.AddWithValue("$text", text);
        statement.Parameters.AddWithValue("$namespace", entityNamespace);
        statement.Parameters.AddWithValue("$now", Format(now));
        await statement.ExecuteNonQueryAsync(token);
        return (id, true);
    }

    private static async Task<StatementRecord?> ReadStatementAsync(SqliteConnection connection, SqliteTransaction? transaction,
        long id, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,text,namespace,created_at,updated_at FROM statements WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token)
            ? new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture))
            : null;
    }

    private static void ValidateAtomicEntityName(string canonicalName, string className, string? sourceStatement = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        var name = canonicalName.Trim();
        if (sourceStatement is not null && string.Equals(name, sourceStatement.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Entity canonicalName must not contain the complete source statement. Split it into atomic terms.");
        var error = GetAtomicEntityNameError(name, className);
        if (error is not null) throw new ArgumentException(error);
    }

    private static string? GetAtomicEntityNameError(string name, string className)
    {
        if (className is "Statement" or "StatementRef")
            return "Statement and StatementRef are reserved; store original text in statement.";
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
}
