using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Kotodama;

/// <summary>namespace統合の入力です。dryRun既定trueで、実行時はexpectedCountに試算のtotalAffectedを指定します。</summary>
public sealed record MergeNamespaceInput(string SourceNamespace, string TargetNamespace, bool DryRun = true, int? ExpectedCount = null);

/// <summary>namespace統合の試算・実行結果です。</summary>
public sealed record MergeNamespaceResult(
    bool DryRun, bool Applied, string SourceNamespace, string TargetNamespace,
    int EntitiesMoved, int EntitiesMerged, int InputsMoved, int TagsMoved, int TotalAffected,
    IReadOnlyList<long> MergedEntityIds, IReadOnlyList<string> Conflicts);

public sealed partial class KnowledgeStore
{
    /// <summary>
    /// sourceのEntity・知識入力・タグをtargetへ移します。同名・同classのEntityがtargetにあれば参照を付け替えて統合します。
    /// 衝突がある場合と、実行時にexpectedCountが試算と異なる場合は何も変更しません。
    /// </summary>
    public async Task<MergeNamespaceResult> MergeNamespaceAsync(MergeNamespaceInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SourceNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TargetNamespace);
        if (string.Equals(input.SourceNamespace, input.TargetNamespace, StringComparison.Ordinal))
            throw new ArgumentException("sourceNamespace and targetNamespace must differ.");
        if (!input.DryRun && input.ExpectedCount is null or < 0)
            throw new ArgumentException("expectedCount is required when dryRun is false.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var plan = await PlanNamespaceMergeAsync(connection, transaction, input.SourceNamespace, input.TargetNamespace, cancellationToken);
        if (plan.TotalAffected == 0) throw new ArgumentException($"sourceNamespace '{input.SourceNamespace}' has no entities, knowledge inputs or tags.");

        MergeNamespaceResult Result(bool applied) => new(input.DryRun, applied, input.SourceNamespace, input.TargetNamespace,
            plan.MoveCount, plan.Merges.Count, plan.InputCount, plan.TagCount, plan.TotalAffected,
            [.. plan.Merges.Select(merge => merge.SourceId)], plan.Conflicts);

        if (input.DryRun || plan.Conflicts.Count > 0) return Result(false);
        if (input.ExpectedCount != plan.TotalAffected) throw new ArgumentException("expectedCount does not match the current totalAffected.");

        foreach (var (sourceId, targetId) in plan.Merges)
            await MergeEntityReferencesAsync(connection, transaction, sourceId, targetId, cancellationToken);

        await ExecuteAsync(connection, transaction, """
            UPDATE entities SET namespace=$target,updated_at=$now WHERE namespace=$source;
            UPDATE knowledge_inputs SET namespace=$target,updated_at=$now WHERE namespace=$source;
            UPDATE tag_names SET namespace=$target WHERE namespace=$source;
            UPDATE tags SET namespace=$target WHERE namespace=$source;
            """, cancellationToken, ("$source", input.SourceNamespace), ("$target", input.TargetNamespace), ("$now", Format(Now())));

        await VerifyNamespaceMergeAsync(connection, transaction, input.SourceNamespace, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result(true);
    }

    private sealed record NamespaceMergePlan(int MoveCount, IReadOnlyList<(long SourceId, long TargetId)> Merges,
        int InputCount, int TagCount, IReadOnlyList<string> Conflicts)
    {
        public int TotalAffected => MoveCount + Merges.Count + InputCount + TagCount;
    }

    private static async Task<NamespaceMergePlan> PlanNamespaceMergeAsync(SqliteConnection connection, SqliteTransaction transaction,
        string source, string target, CancellationToken token)
    {
        var conflicts = new List<string>();
        var merges = new List<(long, long)>();
        var moveCount = 0;

        await using (var command = Command(connection, transaction, """
            SELECT s.id,s.class_name,s.canonical_name,
                   (SELECT COUNT(*) FROM entities t WHERE t.namespace=$target AND t.canonical_name=s.canonical_name AND t.class_name=s.class_name),
                   (SELECT MIN(t.id) FROM entities t WHERE t.namespace=$target AND t.canonical_name=s.canonical_name AND t.class_name=s.class_name)
            FROM entities s WHERE s.namespace=$source ORDER BY s.id
            """, ("$source", source), ("$target", target)))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                var matches = reader.GetInt64(3);
                if (matches == 0) { moveCount++; continue; }
                var label = $"{reader.GetString(1)}:{reader.GetString(2)} (id {reader.GetInt64(0)})";
                if (matches > 1) conflicts.Add($"ambiguous_target_entity {label}");
                else merges.Add((reader.GetInt64(0), reader.GetInt64(4)));
            }
        }

        foreach (var (sourceId, targetId) in merges)
        {
            await using var events = Command(connection, transaction,
                "SELECT COUNT(*) FROM events WHERE entity_id IN($source,$target)", ("$source", sourceId), ("$target", targetId));
            if (Convert.ToInt64(await events.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) > 1)
                conflicts.Add($"event_entity_merge {sourceId}->{targetId}");

            await using var symmetricSelfRelation = Command(connection, transaction, """
                SELECT COUNT(*) FROM symmetric_relations
                WHERE (entity_a_id=$source AND entity_b_id=$target) OR (entity_a_id=$target AND entity_b_id=$source)
                """, ("$source", sourceId), ("$target", targetId));
            if (Convert.ToInt64(await symmetricSelfRelation.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) > 0)
                conflicts.Add($"symmetric_self_relation {sourceId}->{targetId}");
        }

        await using (var tagConflicts = Command(connection, transaction, """
            SELECT s.name FROM tag_names s JOIN tag_names t ON t.namespace=$target AND t.normalized_name=s.normalized_name
            WHERE s.namespace=$source ORDER BY s.normalized_name
            """, ("$source", source), ("$target", target)))
        await using (var reader = await tagConflicts.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token)) conflicts.Add($"tag_name_conflict {reader.GetString(0)}");
        }

        var inputCount = await CountAsync(connection, transaction, "SELECT COUNT(*) FROM knowledge_inputs WHERE namespace=$source", source, token);
        var tagCount = await CountAsync(connection, transaction, "SELECT COUNT(*) FROM tags WHERE namespace=$source", source, token);
        return new(moveCount, merges, inputCount, tagCount, conflicts);
    }

    /// <summary>sourceIdへの全参照をtargetIdへ付け替え、Claim検索投影を再計算してからsourceIdを削除します。</summary>
    private static async Task MergeEntityReferencesAsync(SqliteConnection connection, SqliteTransaction transaction,
        long sourceId, long targetId, CancellationToken token)
    {
        await ExecuteAsync(connection, transaction, $$"""
            CREATE TEMP TABLE IF NOT EXISTS namespace_merge_claims(claim_id INTEGER PRIMARY KEY);
            DELETE FROM namespace_merge_claims;
            INSERT OR IGNORE INTO namespace_merge_claims(claim_id)
                SELECT claim_id FROM claim_search WHERE subject_id=$source OR object_id=$source;

            UPDATE directed_relations SET subject_id=$target WHERE subject_id=$source;
            UPDATE directed_relations SET object_id=$target WHERE object_id=$source;
            UPDATE symmetric_relations
            SET entity_a_id=MIN(CASE WHEN entity_a_id=$source THEN $target ELSE entity_a_id END,
                                CASE WHEN entity_b_id=$source THEN $target ELSE entity_b_id END),
                entity_b_id=MAX(CASE WHEN entity_a_id=$source THEN $target ELSE entity_a_id END,
                                CASE WHEN entity_b_id=$source THEN $target ELSE entity_b_id END)
            WHERE entity_a_id=$source OR entity_b_id=$source;
            INSERT INTO input_terms(input_id,term_entity_id,occurrence_count)
                SELECT input_id,$target,occurrence_count FROM input_terms WHERE term_entity_id=$source
                ON CONFLICT(input_id,term_entity_id) DO UPDATE SET occurrence_count=occurrence_count+excluded.occurrence_count;
            DELETE FROM input_terms WHERE term_entity_id=$source;
            UPDATE claims SET knowledge_subject_id=$target WHERE knowledge_subject_id=$source;
            UPDATE sources SET author_entity_id=$target WHERE author_entity_id=$source;
            UPDATE events SET actor_id=$target WHERE actor_id=$source;
            UPDATE events SET object_id=$target WHERE object_id=$source;
            UPDATE events SET entity_id=$target WHERE entity_id=$source;

            INSERT OR REPLACE INTO claim_search({{ClaimSearchColumns}})
            {{ClaimSearchSelect}} WHERE c.id IN(SELECT claim_id FROM namespace_merge_claims);
            DELETE FROM namespace_merge_claims;
            DELETE FROM entities WHERE id=$source;
            """, token, ("$source", sourceId), ("$target", targetId));
    }

    private static async Task VerifyNamespaceMergeAsync(SqliteConnection connection, SqliteTransaction transaction, string source, CancellationToken token)
    {
        var remaining = await CountAsync(connection, transaction, """
            SELECT (SELECT COUNT(*) FROM entities WHERE namespace=$source)
                 + (SELECT COUNT(*) FROM knowledge_inputs WHERE namespace=$source)
                 + (SELECT COUNT(*) FROM tags WHERE namespace=$source)
                 + (SELECT COUNT(*) FROM tag_names WHERE namespace=$source)
            """, source, token);
        if (remaining != 0) throw new InvalidOperationException("Namespace merge left rows in the source namespace.");

        await using (var fk = Command(connection, transaction, "PRAGMA foreign_key_check"))
        await using (var reader = await fk.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token)) throw new InvalidOperationException("Namespace merge failed foreign key validation.");
        }

        var orphanProjection = await CountAsync(connection, transaction, """
            SELECT COUNT(*) FROM claim_search cs
            WHERE NOT EXISTS(SELECT 1 FROM entities WHERE id=cs.subject_id) OR NOT EXISTS(SELECT 1 FROM entities WHERE id=cs.object_id)
               OR cs.subject_namespace=$source OR cs.object_namespace=$source
            """, source, token);
        if (orphanProjection != 0) throw new InvalidOperationException("Namespace merge left an inconsistent claim_search projection.");
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<int> CountAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, string source, CancellationToken token)
    {
        await using var command = Command(connection, transaction, sql, ("$source", source));
        return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }
}
