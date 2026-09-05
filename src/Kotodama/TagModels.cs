namespace Kotodama;

/// <summary>namespace内のタグ。統合済みIDはMergedIntoIdへ解決します。</summary>
public sealed record TagRecord(long Id, string Name, string Namespace, IReadOnlyList<string> Aliases, long? MergedIntoId);

/// <summary>対象へ付与されたタグと由来です。</summary>
public sealed record TagAssignment(long TagId, string Name, string Origin, long? SourceStatementId);

/// <summary>タグ検索条件です。名前・IDは解決後に重複排除します。</summary>
public sealed record TagQueryInput(IReadOnlyList<string>? Tags = null, IReadOnlyList<long>? TagIds = null,
    string TagMatch = "any", string Namespace = "global", int Limit = 50, long AfterId = 0,
    bool IncludeRetracted = false, bool IncludeStale = false, DateTimeOffset? ValidAt = null);

/// <summary>保存文のタグ検索結果です。</summary>
public sealed record TaggedStatement(EntityRecord Statement, IReadOnlyList<TagAssignment> Tags);

/// <summary>Claimのタグ検索結果です。</summary>
public sealed record TaggedClaim(ClaimRecord Claim, IReadOnlyList<TagAssignment> Tags);

/// <summary>単件・一括タグ変更。実行時は対象件数の一致を要求します。</summary>
public sealed record SetKnowledgeTagsInput(string TargetKind, IReadOnlyList<long> TagIds,
    IReadOnlyList<long>? TargetIds = null, long? KnowledgeSubjectId = null, string Namespace = "global",
    bool Remove = false, bool DryRun = true, int? ExpectedCount = null);

/// <summary>タグ変更の対象レコード件数です。</summary>
public sealed record TagUpdateResult(int MatchedCount, int ChangedCount, bool DryRun);
