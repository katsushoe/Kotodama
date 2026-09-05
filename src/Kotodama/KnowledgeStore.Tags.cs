using System.Text;
using Microsoft.Data.Sqlite;

namespace Kotodama;

public sealed partial class KnowledgeStore
{
    private const int MaximumTags = 100;

    private static async Task InitializeTagsAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = TagCommand(connection, transaction, """
            CREATE TABLE IF NOT EXISTS tags(
                id INTEGER PRIMARY KEY, namespace TEXT NOT NULL, name TEXT NOT NULL,
                merged_into_id INTEGER REFERENCES tags(id), CHECK(merged_into_id IS NULL OR merged_into_id<>id));
            CREATE TABLE IF NOT EXISTS tag_names(
                namespace TEXT NOT NULL, normalized_name TEXT NOT NULL, name TEXT NOT NULL,
                tag_id INTEGER NOT NULL REFERENCES tags(id), PRIMARY KEY(namespace,normalized_name));
            CREATE INDEX IF NOT EXISTS idx_tag_names_id ON tag_names(tag_id);
            CREATE INDEX IF NOT EXISTS idx_tags_namespace ON tags(namespace,id);
            CREATE TABLE IF NOT EXISTS statement_tags(
                statement_id INTEGER NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
                tag_id INTEGER NOT NULL REFERENCES tags(id), origin TEXT NOT NULL CHECK(origin IN('remember','manual')),
                PRIMARY KEY(statement_id,tag_id,origin));
            CREATE INDEX IF NOT EXISTS idx_statement_tags_tag ON statement_tags(tag_id,statement_id);
            CREATE TABLE IF NOT EXISTS claim_tags(
                claim_id INTEGER NOT NULL REFERENCES claims(id) ON DELETE CASCADE,
                tag_id INTEGER NOT NULL REFERENCES tags(id), origin TEXT NOT NULL CHECK(origin IN('inherited','manual')),
                source_statement_id INTEGER REFERENCES entities(id), PRIMARY KEY(claim_id,tag_id,origin));
            CREATE INDEX IF NOT EXISTS idx_claim_tags_tag ON claim_tags(tag_id,claim_id);
            """);
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }

    private static SqliteCommand TagCommand(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static string NormalizeTagName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Normalize(NormalizationForm.FormKC).Trim();
        if (normalized.Length is 0 or > 128 || normalized.Any(char.IsControl))
            throw new ArgumentException("Tag names must contain 1 to 128 characters without control characters.");
        return normalized;
    }

    private static IReadOnlyList<string> NormalizeTagNames(IReadOnlyList<string>? names)
    {
        if (names is null) return [];
        if (names.Count > MaximumTags) throw new ArgumentException("At most 100 tags are allowed.");
        return names.Select(NormalizeTagName).DistinctBy(x => x.ToUpperInvariant()).ToArray();
    }

    private static async Task<long?> FindTagNameAsync(SqliteConnection connection, SqliteTransaction transaction,
        string name, string entityNamespace, CancellationToken token)
    {
        await using var command = TagCommand(connection, transaction,
            "SELECT tag_id FROM tag_names WHERE namespace=$namespace AND normalized_name=$name",
            ("$namespace", entityNamespace), ("$name", NormalizeTagName(name).ToUpperInvariant()));
        return await command.ExecuteScalarAsync(token) is long id ? id : null;
    }

    private static async Task<long> ResolveTagIdAsync(SqliteConnection connection, SqliteTransaction transaction,
        long id, string entityNamespace, CancellationToken token)
    {
        while (true)
        {
            await using var command = TagCommand(connection, transaction,
                "SELECT merged_into_id FROM tags WHERE id=$id AND namespace=$namespace", ("$id", id), ("$namespace", entityNamespace));
            var result = await command.ExecuteScalarAsync(token);
            if (result is null) throw new ArgumentException("Tag ID not found in namespace.");
            if (result is not long next) return id;
            id = next;
        }
    }

    private static async Task<TagRecord> ReadTagAsync(SqliteConnection connection, SqliteTransaction transaction, long id, CancellationToken token)
    {
        await using var command = TagCommand(connection, transaction, "SELECT name,namespace,merged_into_id FROM tags WHERE id=$id", ("$id", id));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new ArgumentException("Tag ID not found.");
        var name = reader.GetString(0);
        var entityNamespace = reader.GetString(1);
        long? merged = reader.IsDBNull(2) ? null : reader.GetInt64(2);
        await reader.DisposeAsync();
        await using var aliases = TagCommand(connection, transaction,
            "SELECT name FROM tag_names WHERE tag_id=$id AND normalized_name<>$name ORDER BY normalized_name",
            ("$id", id), ("$name", name.ToUpperInvariant()));
        await using var aliasReader = await aliases.ExecuteReaderAsync(token);
        var names = new List<string>();
        while (await aliasReader.ReadAsync(token)) names.Add(aliasReader.GetString(0));
        return new(id, name, entityNamespace, names, merged);
    }

    private static async Task<long> GetOrCreateTagAsync(SqliteConnection connection, SqliteTransaction transaction,
        string name, string entityNamespace, CancellationToken token)
    {
        name = NormalizeTagName(name);
        if (await FindTagNameAsync(connection, transaction, name, entityNamespace, token) is long existing) return existing;
        await using var command = TagCommand(connection, transaction,
            "INSERT INTO tags(namespace,name) VALUES($namespace,$name); SELECT last_insert_rowid();",
            ("$namespace", entityNamespace), ("$name", name));
        var id = (long)(await command.ExecuteScalarAsync(token))!;
        await AddTagNameAsync(connection, transaction, id, name, entityNamespace, token);
        return id;
    }

    private static async Task AddTagNameAsync(SqliteConnection connection, SqliteTransaction transaction,
        long id, string name, string entityNamespace, CancellationToken token)
    {
        name = NormalizeTagName(name);
        var owner = await FindTagNameAsync(connection, transaction, name, entityNamespace, token);
        if (owner is not null && owner != id) throw new ArgumentException("Tag name already belongs to another tag.");
        await using var command = TagCommand(connection, transaction,
            "INSERT INTO tag_names(namespace,normalized_name,name,tag_id) VALUES($namespace,$key,$name,$id) ON CONFLICT(namespace,normalized_name) DO NOTHING",
            ("$namespace", entityNamespace), ("$key", name.ToUpperInvariant()), ("$name", name), ("$id", id));
        await command.ExecuteNonQueryAsync(token);
    }

    /// <summary>タグ名・別名を再利用し、なければタグを作成します。</summary>
    public async Task<TagRecord> CreateTagAsync(string name, string entityNamespace = "global", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityNamespace);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var id = await GetOrCreateTagAsync(connection, transaction, name, entityNamespace, cancellationToken);
        var result = await ReadTagAsync(connection, transaction, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>統合済みIDを含むタグ一覧をページ単位で返します。</summary>
    public async Task<IReadOnlyList<TagRecord>> ListTagsAsync(string entityNamespace = "global", long afterId = 0, int limit = 50, CancellationToken cancellationToken = default)
    {
        ValidateTagPage(entityNamespace, afterId, limit);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var command = TagCommand(connection, transaction,
            "SELECT id FROM tags WHERE namespace=$namespace AND id>$after ORDER BY id LIMIT $limit",
            ("$namespace", entityNamespace), ("$after", afterId), ("$limit", limit));
        var ids = await ReadTagTargetIdsAsync(command, cancellationToken);
        var results = new List<TagRecord>();
        foreach (var id in ids) results.Add(await ReadTagAsync(connection, transaction, id, cancellationToken));
        return results;
    }

    /// <summary>IDを維持して改名し、旧名を別名として残します。</summary>
    public Task<TagRecord> RenameTagAsync(long tagId, string name, string entityNamespace = "global", CancellationToken cancellationToken = default) =>
        ChangeTagNameAsync(tagId, name, entityNamespace, true, cancellationToken);

    /// <summary>タグIDへ別名を追加します。</summary>
    public Task<TagRecord> AddTagAliasAsync(long tagId, string alias, string entityNamespace = "global", CancellationToken cancellationToken = default) =>
        ChangeTagNameAsync(tagId, alias, entityNamespace, false, cancellationToken);

    private async Task<TagRecord> ChangeTagNameAsync(long tagId, string name, string entityNamespace, bool rename, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityNamespace);
        name = NormalizeTagName(name);
        await using var connection = await OpenAsync(token);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var id = await ResolveTagIdAsync(connection, transaction, tagId, entityNamespace, token);
        await AddTagNameAsync(connection, transaction, id, name, entityNamespace, token);
        if (rename)
        {
            await using var command = TagCommand(connection, transaction, "UPDATE tags SET name=$name WHERE id=$id", ("$name", name), ("$id", id));
            await command.ExecuteNonQueryAsync(token);
        }
        var result = await ReadTagAsync(connection, transaction, id, token);
        await transaction.CommitAsync(token);
        return result;
    }

    /// <summary>同一namespaceのタグを原子的に統合し、旧IDを維持します。</summary>
    public async Task<TagRecord> MergeTagsAsync(long sourceTagId, long targetTagId, string entityNamespace = "global", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityNamespace);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var source = await ResolveTagIdAsync(connection, transaction, sourceTagId, entityNamespace, cancellationToken);
        var target = await ResolveTagIdAsync(connection, transaction, targetTagId, entityNamespace, cancellationToken);
        if (source != target)
        {
            await using var command = TagCommand(connection, transaction, """
                INSERT INTO statement_tags(statement_id,tag_id,origin)
                    SELECT statement_id,$target,origin FROM statement_tags WHERE tag_id=$source
                    ON CONFLICT(statement_id,tag_id,origin) DO NOTHING;
                INSERT INTO claim_tags(claim_id,tag_id,origin,source_statement_id)
                    SELECT claim_id,$target,origin,source_statement_id FROM claim_tags WHERE tag_id=$source
                    ON CONFLICT(claim_id,tag_id,origin) DO NOTHING;
                DELETE FROM statement_tags WHERE tag_id=$source;
                DELETE FROM claim_tags WHERE tag_id=$source;
                UPDATE tag_names SET tag_id=$target WHERE tag_id=$source;
                UPDATE tags SET merged_into_id=$target WHERE id=$source OR merged_into_id=$source;
                """, ("$source", source), ("$target", target));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var result = await ReadTagAsync(connection, transaction, target, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<bool> ApplyRememberTagsAsync(SqliteConnection connection, SqliteTransaction transaction,
        RememberKnowledgeResult result, IReadOnlyList<string>? names, string entityNamespace, CancellationToken token)
    {
        var changed = false;
        foreach (var name in NormalizeTagNames(names))
        {
            var id = await GetOrCreateTagAsync(connection, transaction, name, entityNamespace, token);
            changed |= await InsertTagAssignmentAsync(connection, transaction, "statement", result.StatementId, id, "remember", null, token);
            foreach (var claimId in result.ClaimIds.Append(result.ClaimId).Distinct())
                changed |= await InsertTagAssignmentAsync(connection, transaction, "claim", claimId, id, "inherited", result.StatementId, token);
        }
        return changed;
    }

    private static async Task<bool> InsertTagAssignmentAsync(SqliteConnection connection, SqliteTransaction transaction,
        string kind, long targetId, long tagId, string origin, long? statementId, CancellationToken token)
    {
        var sql = kind == "statement"
            ? "INSERT INTO statement_tags(statement_id,tag_id,origin) VALUES($target,$tag,$origin) ON CONFLICT(statement_id,tag_id,origin) DO NOTHING"
            : "INSERT INTO claim_tags(claim_id,tag_id,origin,source_statement_id) VALUES($target,$tag,$origin,$statement) ON CONFLICT(claim_id,tag_id,origin) DO NOTHING";
        await using var command = TagCommand(connection, transaction, sql,
            ("$target", targetId), ("$tag", tagId), ("$origin", origin), ("$statement", statementId));
        return await command.ExecuteNonQueryAsync(token) > 0;
    }

    private static void ValidateTagPage(string entityNamespace, long afterId, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityNamespace);
        if (afterId < 0 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit), "afterId must be nonnegative and limit must be 1 to 200.");
    }

    private static async Task<List<long>> ReadTagTargetIdsAsync(SqliteCommand command, CancellationToken token)
    {
        await using var reader = await command.ExecuteReaderAsync(token);
        var ids = new List<long>();
        while (await reader.ReadAsync(token)) ids.Add(reader.GetInt64(0));
        return ids;
    }
}
