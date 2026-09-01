using System.Text.Json.Serialization;

namespace Kotodama;

/// <summary>Relation の方向性です。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RelationKind>))]
public enum RelationKind { Directed, Symmetric }

/// <summary>Claim の極性です。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Polarity>))]
public enum Polarity { Positive, Negative }

/// <summary>知識の鮮度ポリシーです。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FreshnessPolicy>))]
public enum FreshnessPolicy { Permanent, Periodic, Volatile }

/// <summary>Claim の状態です。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClaimStatus>))]
public enum ClaimStatus { Active, Retracted, Stale }

/// <summary>dreamの一時テーブル格納先です。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DreamTempStore>))]
public enum DreamTempStore { Default, Memory, File }

/// <summary>Entity の登録要求です。</summary>
public sealed record EntityInput(string CanonicalName, string ClassName = "Entity", string Namespace = "global", string? Metadata = null);

/// <summary>RelationType の登録要求です。</summary>
public sealed record RelationTypeInput(string CanonicalName, string Category, RelationKind Kind, bool AllowStrength = false, string? InverseName = null, FreshnessPolicy FreshnessPolicy = FreshnessPolicy.Permanent, long? RefreshAfterSeconds = null, string? Description = null);

/// <summary>RelationType の更新要求です。</summary>
public sealed record RelationTypeUpdate(string CanonicalName, string Category, bool AllowStrength = false, string? InverseName = null, FreshnessPolicy FreshnessPolicy = FreshnessPolicy.Permanent, long? RefreshAfterSeconds = null, string? Description = null);

/// <summary>Source の登録要求です。</summary>
public sealed record SourceInput(string SourceType, string? Uri = null, string? ExternalId = null, string? Title = null, long? AuthorEntityId = null, double? Reliability = null, string? Metadata = null);

/// <summary>Knowledge Candidate です。</summary>
public sealed record ClaimCandidate(long SubjectId, long ObjectId, string RelationType, Polarity Polarity = Polarity.Positive, double Confidence = 1, double? AttributionConfidence = null, double? Strength = null, long? KnowledgeSubjectId = null, SourceInput? Source = null, string AssertionType = "user_claim", DateTimeOffset? ObservedAt = null, DateTimeOffset? ValidFrom = null, DateTimeOffset? ValidTo = null, DateTimeOffset? LastConfirmedAt = null);

/// <summary>Entity の検索結果です。</summary>
public sealed record EntityRecord(long Id, string CanonicalName, string ClassName, string Namespace, string? Metadata, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Relation と Claim を結合した検索結果です。</summary>
public sealed record ClaimRecord(long ClaimId, long RelationId, string RelationType, RelationKind Kind, long SubjectId, long ObjectId, Polarity Polarity, double Confidence, double? AttributionConfidence, double? Strength, long? KnowledgeSubjectId, long? SourceId, string AssertionType, DateTimeOffset ObservedAt, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, DateTimeOffset? LastConfirmedAt, ClaimStatus Status);

/// <summary>操作結果です。</summary>
public sealed record OperationResult(bool Ok, string Status, string? Reason = null, long? Id = null);

/// <summary>自然文の知識保存要求です。</summary>
public sealed record RememberKnowledgeInput(
    string Text,
    string Namespace = "global",
    double Confidence = 1,
    SourceInput? Source = null,
    DateTimeOffset? ObservedAt = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null);

/// <summary>自然文の知識保存結果です。</summary>
public sealed record RememberKnowledgeResult(
    bool Ok,
    string Status,
    long SubjectId,
    long StatementId,
    long ClaimId,
    int CreatedEntities,
    bool CreatedRelationType);

/// <summary>dream の実行結果です。</summary>
public sealed record DreamResult(int Examined, int MarkedStale, DateTimeOffset EvaluatedAt)
{
    /// <summary>confidenceを減衰したClaim数です。</summary>
    public int ReducedConfidence { get; init; }
}

/// <summary>Event の登録要求です。</summary>
public sealed record EventInput(string CanonicalName, long? ActorId, DateTimeOffset OccurredAt, string Action, long? ObjectId = null, string? ObjectValue = null, string Namespace = "global", string? Metadata = null);

/// <summary>Event の登録結果です。</summary>
public sealed record EventRecord(long EntityId, string CanonicalName, long? ActorId, DateTimeOffset OccurredAt, string Action, long? ObjectId, string? ObjectValue);
