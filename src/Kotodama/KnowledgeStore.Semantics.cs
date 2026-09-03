using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Kotodama;

public sealed partial class KnowledgeStore
{
    /// <summary>現在有効なequalsの反射・対称・推移閉包を返します。Entityは物理統合しません。</summary>
    public async Task<IReadOnlyList<EntityRecord>> GetEquivalentEntitiesAsync(long entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE equivalent(id) AS (
                SELECT id FROM entities WHERE id=$id
                UNION
                SELECT CASE WHEN s.entity_a_id=e.id THEN s.entity_b_id ELSE s.entity_a_id END
                FROM equivalent e
                JOIN symmetric_relations s ON s.entity_a_id=e.id OR s.entity_b_id=e.id
                JOIN relations r ON r.id=s.relation_id
                JOIN relation_types rt ON rt.id=r.relation_type_id AND rt.canonical_name='equals'
                JOIN claims c ON c.relation_id=r.id AND c.polarity='positive' AND c.status='active'
                JOIN entities a ON a.id=s.entity_a_id
                JOIN entities b ON b.id=s.entity_b_id AND b.namespace=a.namespace
                WHERE (c.valid_from IS NULL OR c.valid_from<=$now) AND (c.valid_to IS NULL OR c.valid_to>$now)
            )
            SELECT id,canonical_name,class_name,namespace,metadata,created_at,updated_at
            FROM entities WHERE id IN(SELECT id FROM equivalent) ORDER BY id
            """;
        command.Parameters.AddWithValue("$id", entityId);
        command.Parameters.AddWithValue("$now", Format(Now()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<EntityRecord>();
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadEntity(reader));
        return results;
    }

    private async Task<IReadOnlyList<EntityRecord>> ExpandEntitySearchAsync(SqliteConnection connection, List<EntityRecord> matches, int limit, CancellationToken token)
    {
        var results = matches.Select(x => x with { Match = new("name", x.Id, []) }).ToList();
        var visited = results.Select(x => x.Id).ToHashSet();
        var queue = new Queue<EntityRecord>(results);
        var now = Now();
        while (queue.TryDequeue(out var current) && results.Count < limit)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT e.id,e.canonical_name,e.class_name,e.namespace,e.metadata,e.created_at,e.updated_at,
                       MIN(c.id),rt.canonical_name
                FROM claims c JOIN relations r ON r.id=c.relation_id
                JOIN relation_types rt ON rt.id=r.relation_type_id
                LEFT JOIN symmetric_relations s ON s.relation_id=r.id
                LEFT JOIN directed_relations d ON d.relation_id=r.id
                JOIN entities e ON e.id=CASE
                    WHEN s.entity_a_id=$id THEN s.entity_b_id WHEN s.entity_b_id=$id THEN s.entity_a_id
                    WHEN d.subject_id=$id THEN d.object_id WHEN d.object_id=$id THEN d.subject_id END
                LEFT JOIN entities g ON g.id=d.object_id
                WHERE e.namespace=$namespace AND c.status='active' AND c.polarity='positive'
                  AND (c.valid_from IS NULL OR c.valid_from<=$now) AND (c.valid_to IS NULL OR c.valid_to>$now)
                  AND (rt.canonical_name IN('similar_to','equals') OR (rt.canonical_name='member_of' AND g.class_name='SimilarityGroup'))
                GROUP BY e.id,rt.canonical_name ORDER BY e.canonical_name,e.id,rt.canonical_name
                """;
            command.Parameters.AddWithValue("$id", current.Id);
            command.Parameters.AddWithValue("$namespace", current.Namespace);
            command.Parameters.AddWithValue("$now", Format(now));
            await using var reader = await command.ExecuteReaderAsync(token);
            while (results.Count < limit && await reader.ReadAsync(token))
            {
                var neighbor = ReadEntity(reader);
                if (!visited.Add(neighbor.Id)) continue;
                var path = current.Match?.ClaimIds.Append(reader.GetInt64(7)).ToArray() ?? [reader.GetInt64(7)];
                var kind = path.Length == 1 ? reader.GetString(8) : "related_path";
                neighbor = neighbor with { Match = new(kind, current.Match?.MatchedEntityId, path) };
                results.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }
        return results;
    }

    /// <summary>明示指定された2グループを新規グループへ統合します。旧所属は撤回し、履歴を残します。</summary>
    public async Task<SimilarityGroupResult> MergeSimilarityGroupsAsync(long groupAId, long groupBId, string? canonicalName = null, CancellationToken cancellationToken = default)
    {
        if (groupAId == groupBId) throw new ArgumentException("Specify two different groups.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var a = await ReadEntityAsync(connection, transaction, groupAId, cancellationToken);
        var b = await ReadEntityAsync(connection, transaction, groupBId, cancellationToken);
        if (a?.ClassName != "SimilarityGroup" || b?.ClassName != "SimilarityGroup") throw new ArgumentException("Both entities must be SimilarityGroup.");
        if (a.Namespace != b.Namespace) throw new ArgumentException("Groups cannot cross namespaces.");
        var membersA = await ReadGroupMembersAsync(connection, transaction, a, cancellationToken);
        var membersB = await ReadGroupMembersAsync(connection, transaction, b, cancellationToken);
        var count = membersA.Count + membersB.Count;
        var threshold = count == 0 ? DefaultSimilarityThreshold
            : (ReadThreshold(a.Metadata) * membersA.Count + ReadThreshold(b.Metadata) * membersB.Count) / count;
        var name = string.IsNullOrWhiteSpace(canonicalName) ? $"SimilarityGroup:{Guid.NewGuid():N}" : canonicalName.Trim();
        var (groupId, created) = await GetOrCreateEntityAsync(connection, transaction, name, "SimilarityGroup", a.Namespace, Now(), cancellationToken);
        if (!created) throw new ArgumentException("Merged group canonicalName must be new.");
        await SetMetadataAsync(connection, transaction, groupId, JsonSerializer.Serialize(new { threshold }), cancellationToken);
        var type = await GetRelationTypeAsync(connection, transaction, "member_of", cancellationToken)
            ?? throw new InvalidOperationException("member_of is not initialized.");
        var sourceId = await InsertSourceAsync(connection, transaction,
            new("group_merge", Metadata: JsonSerializer.Serialize(new { groupAId, groupBId })), cancellationToken);
        var members = membersA.Union(membersB).Order().ToArray();
        var claims = new List<long>();
        foreach (var member in members)
        {
            var relationId = await GetOrCreateRelationAsync(connection, transaction, type.Id, type.Kind, member, groupId, cancellationToken);
            claims.Add(await InsertClaimAsync(connection, transaction, relationId, sourceId,
                new(member, groupId, "member_of", AssertionType: "group_merge", ObservedAt: Now(), LastConfirmedAt: Now()), cancellationToken));
        }
        await using var retract = connection.CreateCommand();
        retract.Transaction = transaction;
        retract.CommandText = """
            UPDATE claims SET status='retracted',updated_at=$now
            WHERE status<>'retracted' AND relation_id IN(
                SELECT d.relation_id FROM directed_relations d JOIN relations r ON r.id=d.relation_id
                WHERE r.relation_type_id=$type AND d.object_id IN($a,$b))
            """;
        retract.Parameters.AddWithValue("$a", groupAId);
        retract.Parameters.AddWithValue("$b", groupBId);
        retract.Parameters.AddWithValue("$type", type.Id);
        retract.Parameters.AddWithValue("$now", Format(Now()));
        await retract.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(groupId, threshold, members.Length, claims);
    }

    private async Task<HashSet<long>> ReadGroupMembersAsync(SqliteConnection connection, SqliteTransaction transaction, EntityRecord group, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT DISTINCT d.subject_id FROM directed_relations d JOIN relations r ON r.id=d.relation_id
            JOIN relation_types rt ON rt.id=r.relation_type_id AND rt.canonical_name='member_of'
            JOIN claims c ON c.relation_id=r.id AND c.status='active' AND c.polarity='positive'
            JOIN entities e ON e.id=d.subject_id AND e.namespace=$namespace AND e.class_name<>'SimilarityGroup'
            WHERE d.object_id=$group AND (c.valid_from IS NULL OR c.valid_from<=$now) AND (c.valid_to IS NULL OR c.valid_to>$now)
            """;
        command.Parameters.AddWithValue("$group", group.Id);
        command.Parameters.AddWithValue("$namespace", group.Namespace);
        command.Parameters.AddWithValue("$now", Format(Now()));
        await using var reader = await command.ExecuteReaderAsync(token);
        var ids = new HashSet<long>();
        while (await reader.ReadAsync(token)) ids.Add(reader.GetInt64(0));
        return ids;
    }

    private static async Task<bool> IsReservedRelationIdAsync(SqliteConnection connection, long id, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT canonical_name FROM relation_types WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteScalarAsync(token) is string name && IsReservedRelation(name);
    }
}
