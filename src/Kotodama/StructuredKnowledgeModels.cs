using System.ComponentModel;

namespace Kotodama;

/// <summary>抽出済みの概念と関係を伴う知識登録要求です。</summary>
public sealed record StructuredKnowledgeInput(
    [Description("解析中だけMemoryで扱う入力文。DB、Log、応答には保存しません。")]
    string Statement,
    [Description("原文から分解した原子的な概念。各要素は1つの固有名・名詞・短い名詞句・識別子です。")]
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
    [Description("呼び出し元が管理する再入力回数。0が初回、1～3が再入力。最終失敗も未保存で返します。")]
    int RetryCount = 0,
    [Description("namespace内の正規名・別名で指定するタグ。本文を持たない入力単位とClaimへ原子的に付与します。")]
    IReadOnlyList<string>? Tags = null);

/// <summary>keyで関係から参照する概念。既存EntityはentityIdを指定します。</summary>
public sealed record RememberedEntityInput(
    string Key,
    [Description("文章や原文全体ではない、1つの固有名・名詞・短い名詞句・識別子。")]
    string CanonicalName,
    string ClassName = "Entity",
    long? EntityId = null,
    string? Metadata = null);

/// <summary>subject/objectは同じ要求内のEntity keyです。</summary>
public sealed record RememberedRelationInput(string Subject, string Object, string RelationType, Polarity Polarity = Polarity.Positive, double Confidence = 1, double? Strength = null);

/// <summary>検索結果の到達理由。類似経路は類似性の推移を意味しません。</summary>
public sealed record EntitySearchMatch(string Kind, long? MatchedEntityId, IReadOnlyList<long> ClaimIds);

/// <summary>明示的グループ統合の結果です。</summary>
public sealed record SimilarityGroupResult(long GroupId, double Threshold, int MemberCount, IReadOnlyList<long> ClaimIds);
