using System.ComponentModel;

namespace Kotodama;

/// <summary>抽出済みの概念と関係を伴う知識登録要求です。</summary>
public sealed record StructuredKnowledgeInput(
    string Statement,
    IReadOnlyList<RememberedEntityInput> Entities,
    IReadOnlyList<RememberedRelationInput> Relations,
    string? Reason = null,
    string Namespace = "global",
    double Confidence = 1,
    SourceInput? Source = null,
    DateTimeOffset? ObservedAt = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    RememberedEventInput? Event = null,
    [Description("呼び出し元が管理する再入力回数。0が初回、1～3が再入力。3回目の構造エラーは原文保存へ縮退します。")]
    int RetryCount = 0);

/// <summary>keyで関係から参照する概念。既存EntityはentityIdを指定します。</summary>
public sealed record RememberedEntityInput(string Key, string CanonicalName, string ClassName = "Entity", long? EntityId = null, string? Metadata = null);

/// <summary>subject/objectは同じ要求内のEntity keyです。</summary>
public sealed record RememberedRelationInput(string Subject, string Object, string RelationType, Polarity Polarity = Polarity.Positive, double Confidence = 1, double? Strength = null);

/// <summary>検索結果の到達理由。類似経路は類似性の推移を意味しません。</summary>
public sealed record EntitySearchMatch(string Kind, long? MatchedEntityId, IReadOnlyList<long> ClaimIds);

/// <summary>明示的グループ統合の結果です。</summary>
public sealed record SimilarityGroupResult(long GroupId, double Threshold, int MemberCount, IReadOnlyList<long> ClaimIds);
