using Microsoft.Data.Sqlite;

namespace Kotodama;

public sealed partial class KnowledgeStore
{
    /// <summary>本文を持たない知識入力をタグの完全一致・AND/ORで検索します。</summary>
    public async Task<IReadOnlyList<TaggedKnowledgeInput>> QueryTaggedInputsAsync(TagQueryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var ids = await FindTaggedTargetsAsync(connection, transaction, input, "input", cancellationToken);
        var results = new List<TaggedKnowledgeInput>();
        foreach (var id in ids)
        {
            var knowledgeInput = await ReadKnowledgeInputAsync(connection, transaction, id, cancellationToken);
            results.Add(new(knowledgeInput!, await ReadTagAssignmentsAsync(connection, transaction, "input", id, cancellationToken)));
        }
        return results;
    }

    /// <summary>Claimをタグの完全一致・AND/ORと状態・有効時点で検索します。</summary>
    public async Task<IReadOnlyList<TaggedClaim>> QueryTaggedClaimsAsync(TagQueryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var ids = await FindTaggedTargetsAsync(connection, transaction, input, "claim", cancellationToken);
        var results = new List<TaggedClaim>();
        foreach (var id in ids)
        {
            await using var command = TagCommand(connection, transaction, QuerySql + " WHERE p.claim_id=$id", ("$id", id));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var claim = ReadClaim(reader);
            await reader.DisposeAsync();
            results.Add(new(claim, await ReadTagAssignmentsAsync(connection, transaction, "claim", id, cancellationToken)));
        }
        return results;
    }

    private static async Task<IReadOnlyList<long>> FindTaggedTargetsAsync(SqliteConnection connection, SqliteTransaction transaction,
        TagQueryInput input, string kind, CancellationToken token)
    {
        ValidateTagPage(input.Namespace, input.AfterId, input.Limit);
        if (input.TagMatch is not ("any" or "all")) throw new ArgumentException("tagMatch must be any or all.");
        var names = NormalizeTagNames(input.Tags);
        var requestedIds = input.TagIds ?? [];
        if (requestedIds.Count + names.Count is 0 or > MaximumTags) throw new ArgumentException("Specify 1 to 100 tags or tagIds.");
        var ids = new HashSet<long>();
        var unknown = false;
        foreach (var name in names)
        {
            if (await FindTagNameAsync(connection, transaction, name, input.Namespace, token) is long id) ids.Add(id);
            else unknown = true;
        }
        foreach (var id in requestedIds) ids.Add(await ResolveTagIdAsync(connection, transaction, id, input.Namespace, token));
        if (ids.Count == 0 || unknown && input.TagMatch == "all") return [];

        var sql = kind == "input"
            ? "SELECT e.id FROM knowledge_inputs e WHERE e.namespace=$namespace AND e.id>$after"
            : "SELECT c.id FROM claims c WHERE c.id>$after AND ($retracted=1 OR c.status<>'retracted') AND ($stale=1 OR c.status<>'stale') AND ($at IS NULL OR (c.valid_from IS NULL OR c.valid_from<=$at) AND (c.valid_to IS NULL OR c.valid_to>$at))";
        var target = kind == "input" ? "e.id" : "c.id";
        // kindは内部定数だけを渡し、利用者の値をSQL識別子へ展開しません。
        var table = kind == "input" ? "input_tags" : "claim_tags";
        var column = kind == "input" ? "input_id" : "claim_id";
        await using var command = TagCommand(connection, transaction, sql, ("$namespace", input.Namespace),
            ("$after", input.AfterId), ("$limit", input.Limit), ("$count", input.TagMatch == "all" ? ids.Count : 1),
            ("$retracted", input.IncludeRetracted), ("$stale", input.IncludeStale), ("$at", input.ValidAt is null ? null : Format(input.ValidAt.Value)));
        var parameters = AddTagIdParameters(command, ids, "tag");
        command.CommandText += $" AND (SELECT COUNT(DISTINCT a.tag_id) FROM {table} a JOIN tags t ON t.id=a.tag_id WHERE a.{column}={target} AND t.namespace=$namespace AND a.tag_id IN ({parameters})) >= $count ORDER BY {target} LIMIT $limit";
        return await ReadTagTargetIdsAsync(command, token);
    }

    private static string AddTagIdParameters(SqliteCommand command, IEnumerable<long> ids, string prefix)
    {
        var parameters = new List<string>();
        foreach (var id in ids)
        {
            var name = "$" + prefix + parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            command.Parameters.AddWithValue(name, id);
            parameters.Add(name);
        }
        return string.Join(',', parameters);
    }

    private static async Task<IReadOnlyList<TagAssignment>> ReadTagAssignmentsAsync(SqliteConnection connection, SqliteTransaction transaction,
        string kind, long id, CancellationToken token)
    {
        var sql = kind == "input"
            ? "SELECT t.id,t.name,a.origin,NULL FROM input_tags a JOIN tags t ON t.id=a.tag_id WHERE a.input_id=$id ORDER BY t.id,a.origin"
            : "SELECT t.id,t.name,a.origin,a.source_input_id FROM claim_tags a JOIN tags t ON t.id=a.tag_id WHERE a.claim_id=$id ORDER BY t.id,a.origin";
        await using var command = TagCommand(connection, transaction, sql, ("$id", id));
        await using var reader = await command.ExecuteReaderAsync(token);
        var results = new List<TagAssignment>();
        while (await reader.ReadAsync(token)) results.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt64(3)));
        return results;
    }

    /// <summary>明示対象またはknowledgeSubjectId条件でタグを付与・解除します。</summary>
    public async Task<TagUpdateResult> SetKnowledgeTagsAsync(SetKnowledgeTagsInput input, CancellationToken cancellationToken = default)
    {
        ValidateTagUpdate(input);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var tags = new HashSet<long>();
        foreach (var tag in input.TagIds) tags.Add(await ResolveTagIdAsync(connection, transaction, tag, input.Namespace, cancellationToken));
        var ids = await FindTagUpdateTargetsAsync(connection, transaction, input, cancellationToken);
        if (!input.DryRun && input.ExpectedCount != ids.Count) throw new ArgumentException("expectedCount does not match the current target count.");
        if (input.DryRun) return new(ids.Count, 0, true);
        var changed = 0;
        foreach (var id in ids)
        {
            if (await UpdateTargetTagsAsync(connection, transaction, input, tags, id, cancellationToken)) changed++;
        }
        await transaction.CommitAsync(cancellationToken);
        return new(ids.Count, changed, false);
    }

    private static void ValidateTagUpdate(SetKnowledgeTagsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.TagIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Namespace);
        if (input.TargetKind is not ("input" or "claim")) throw new ArgumentException("targetKind must be input or claim.");
        if (input.TagIds.Count is 0 or > MaximumTags) throw new ArgumentException("Specify 1 to 100 tagIds.");
        if ((input.TargetIds is null) == (input.KnowledgeSubjectId is null)) throw new ArgumentException("Specify targetIds or knowledgeSubjectId, exclusively.");
        if (input.TargetIds is { Count: 0 or > 200 }) throw new ArgumentException("Specify 1 to 200 targetIds.");
        if (!input.DryRun && (input.ExpectedCount is null or < 0)) throw new ArgumentException("expectedCount is required when dryRun is false.");
    }

    private static async Task<List<long>> FindTagUpdateTargetsAsync(SqliteConnection connection, SqliteTransaction transaction,
        SetKnowledgeTagsInput input, CancellationToken token)
    {
        if (input.KnowledgeSubjectId is long subjectId)
        {
            var subject = await ReadEntityAsync(connection, transaction, subjectId, token);
            if (subject is null || subject.Namespace != input.Namespace) throw new ArgumentException("knowledgeSubjectId not found in namespace.");
        }
        var sql = input.TargetKind == "input"
            ? "SELECT DISTINCT e.id FROM knowledge_inputs e WHERE e.namespace=$namespace AND ($subject IS NULL OR EXISTS(SELECT 1 FROM claims c JOIN sources src ON src.id=c.source_id WHERE src.source_input_id=e.id AND c.knowledge_subject_id=$subject))"
            : "SELECT p.claim_id FROM claim_search p WHERE p.subject_namespace=$namespace AND p.object_namespace=$namespace AND ($subject IS NULL OR p.knowledge_subject_id=$subject)";
        var target = input.TargetKind == "input" ? "e.id" : "p.claim_id";
        await using var command = TagCommand(connection, transaction, sql, ("$namespace", input.Namespace), ("$subject", input.KnowledgeSubjectId));
        if (input.TargetIds is not null) command.CommandText += $" AND {target} IN ({AddTagIdParameters(command, input.TargetIds.Distinct(), "target")})";
        command.CommandText += $" ORDER BY {target}";
        var ids = await ReadTagTargetIdsAsync(command, token);
        if (input.TargetIds is not null && ids.Count != input.TargetIds.Distinct().Count())
            throw new ArgumentException("One or more target IDs do not match targetKind and namespace.");
        return ids;
    }

    private static async Task<bool> UpdateTargetTagsAsync(SqliteConnection connection, SqliteTransaction transaction,
        SetKnowledgeTagsInput input, HashSet<long> tags, long id, CancellationToken token)
    {
        var changed = false;
        foreach (var tagId in tags)
        {
            if (!input.Remove)
            {
                changed |= await InsertTagAssignmentAsync(connection, transaction, input.TargetKind, id, tagId, "manual", null, token);
                continue;
            }
            var sql = input.TargetKind == "input"
                ? "DELETE FROM input_tags WHERE input_id=$target AND tag_id=$tag"
                : "DELETE FROM claim_tags WHERE claim_id=$target AND tag_id=$tag";
            await using var command = TagCommand(connection, transaction, sql, ("$target", id), ("$tag", tagId));
            changed |= await command.ExecuteNonQueryAsync(token) > 0;
        }
        return changed;
    }
}
