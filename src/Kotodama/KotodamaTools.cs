using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Kotodama;

/// <summary>Kotodama MCP Tool 群です。</summary>
[McpServerToolType]
public sealed class KotodamaTools(KnowledgeStore store)
{
    [McpServerTool(Name = "get_version"), Description("稼働中のKotodamaバージョンを返します。")]
    public static object GetVersion() => new { name = "Kotodama", version = "0.13.0" };

    [McpServerTool(Name = "get_entity"), Description("IDでEntityを取得します。存在しない場合はnullです。")]
    public Task<EntityRecord?> GetEntity(long id, CancellationToken cancellationToken) => store.GetEntityAsync(id, cancellationToken);

    [McpServerTool(Name = "search_entities"), Description("名前の部分一致を優先し、同じnamespaceの有効なPositive similar_to/equalsとSimilarityGroup所属を辿った関連候補を合計limit件まで返します。matchは到達理由とClaim経路です。related_pathは類似性の推移を意味しません。includeRelated=falseで名前一致のみです。")]
    public Task<IReadOnlyList<EntityRecord>> SearchEntities(string query, int limit = 50, bool includeRelated = true, CancellationToken cancellationToken = default) => store.SearchEntitiesAsync(query, limit, cancellationToken, includeRelated);

    [McpServerTool(Name = "get_equivalent_entities", ReadOnly = true), Description("現在有効なPositive equals/canonical_ofの反射・対称・推移閉包を返します。自身を含み、撤回・stale・期限切れの辺は除外します。Entityの物理統合はしません。query_claimsは引き続き明示Claimのみを返します。")]
    public Task<IReadOnlyList<EntityRecord>> GetEquivalentEntities(long entityId, CancellationToken cancellationToken) => store.GetEquivalentEntitiesAsync(entityId, cancellationToken);

    [McpServerTool(Name = "merge_similarity_groups"), Description("明示的な統合依頼時だけ使用します。2つのSimilarityGroupから新規グループを作り、thresholdを各グループの有効な一意メンバー数で加重平均します。重複メンバーは新グループでは1件、両方空なら0.5です。旧所属Claimは撤回されます。自動統合・自動分裂はしません。")]
    public Task<SimilarityGroupResult> MergeSimilarityGroups(long groupAId, long groupBId, string? canonicalName = null, CancellationToken cancellationToken = default) => store.MergeSimilarityGroupsAsync(groupAId, groupBId, canonicalName, cancellationToken);

    [McpServerTool(Name = "propose_claim"), Description("Knowledge Candidateを規則検証し、妥当ならClaimとして保存します。")]
    public Task<OperationResult> ProposeClaim(ClaimCandidate candidate, CancellationToken cancellationToken) => store.ProposeClaimAsync(candidate, cancellationToken);

    [McpServerTool(Name = "remember_knowledge"), Description("input.statementに原文、必須entities/relationsに抽出済み概念・関係を渡します。entitiesはkey,canonicalName,className,任意entityId/metadata、relationsはsubject/objectキー,relationType,polarity,confidence,任意strengthです。目安は概念2件・関係1件、上限100/200。意図的ゼロ件は空配列とreasonを指定します。構造エラーは最大3回修正しretryCountを増加、3回目も失敗すれば原文だけ保存します。DB障害は縮退しません。SourceStatementIdで原文へ追跡可能です。similar_toはstrength=類似度、confidence=判定確信度で推移性なし。equals/canonical_ofはNegative禁止。同一namespaceのみ。SimilarityGroup metadataは固定JSON文字列 {\"threshold\":0.5}（0～1）、不正値は0.5。Event入力も併用可能です。")]
    public Task<RememberKnowledgeResult> RememberKnowledge(StructuredKnowledgeInput input, CancellationToken cancellationToken) => store.RememberStructuredKnowledgeAsync(input, cancellationToken);

    [McpServerTool(Name = "query_events"), Description("構造化Eventをactor、place、期間で検索します。予定の質問では質問文全体の部分一致よりこのToolを優先してください。期間はfrom以上to未満と重なるEventを返します。")]
    public Task<IReadOnlyList<EventSearchRecord>> QueryEvents(string? actor = null, string? place = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string entityNamespace = "global", int limit = 50, CancellationToken cancellationToken = default) => store.QueryEventsAsync(actor, place, from, to, entityNamespace, limit, cancellationToken);

    [McpServerTool(Name = "retract_claim"), Description("Claimを物理削除せずretractedへ変更します。")]
    public Task<OperationResult> RetractClaim(long claimId, CancellationToken cancellationToken) => store.RetractClaimAsync(claimId, cancellationToken);

    [McpServerTool(Name = "reactivate_claim"), Description("撤回またはstaleのClaimを再確認済みのactiveへ戻します。")]
    public Task<OperationResult> ReactivateClaim(long claimId, DateTimeOffset? confirmedAt = null, CancellationToken cancellationToken = default) => store.ReactivateClaimAsync(claimId, confirmedAt, cancellationToken);

    [McpServerTool(Name = "delete_claim"), Description("指定Claimを物理削除します。取り消せません。")]
    public Task<OperationResult> DeleteClaim(long claimId, CancellationToken cancellationToken) => store.DeleteClaimAsync(claimId, cancellationToken);

    [McpServerTool(Name = "query_claims"), Description("ClaimをEntity、RelationType、過去時点で検索します。空配列はunknownです。")]
    public Task<IReadOnlyList<ClaimRecord>> QueryClaims(long? entityId = null, string? relationType = null, DateTimeOffset? validAt = null, bool includeRetracted = false, bool includeStale = false, CancellationToken cancellationToken = default) => store.QueryClaimsAsync(entityId, relationType, validAt, includeRetracted, includeStale, cancellationToken);

    [McpServerTool(Name = "query_relations"), Description("Relationと関連Claimを検索します。")]
    public Task<IReadOnlyList<ClaimRecord>> QueryRelations(long? entityId = null, string? relationType = null, CancellationToken cancellationToken = default) => store.QueryClaimsAsync(entityId, relationType, cancellationToken: cancellationToken);

    [McpServerTool(Name = "get_neighbors"), Description("Entityに接続する有向・対称Relationを返します。")]
    public Task<IReadOnlyList<ClaimRecord>> GetNeighbors(long entityId, CancellationToken cancellationToken) => store.QueryClaimsAsync(entityId, cancellationToken: cancellationToken);

    [McpServerTool(Name = "get_knowledge_context"), Description("Entityの現在有効なKnowledge Contextを返します。")]
    public Task<IReadOnlyList<ClaimRecord>> GetKnowledgeContext(long entityId, CancellationToken cancellationToken) => store.QueryClaimsAsync(entityId, validAt: DateTimeOffset.UtcNow, cancellationToken: cancellationToken);

    [McpServerTool(Name = "run_dream"), Description("期限超過したClaimをfalseにせずstaleへ変更します。")]
    public Task<DreamResult> RunDream(CancellationToken cancellationToken) => store.RunDreamAsync(cancellationToken);

    [McpServerTool(Name = "create_entity"), Description("Entityを登録します。SimilarityGroupはmetadataに固定JSON文字列 {\"threshold\":0.5} を指定します。thresholdは0～1、不正JSON・欠落・範囲外は0.5へ正規化します。")]
    public Task<EntityRecord> CreateEntity(EntityInput input, CancellationToken cancellationToken) => store.CreateEntityAsync(input, cancellationToken);

    [McpServerTool(Name = "create_relation_type"), Description("RelationTypeと規則属性を登録します。")]
    public Task<long> CreateRelationType(RelationTypeInput input, CancellationToken cancellationToken) => store.CreateRelationTypeAsync(input, cancellationToken);

    [McpServerTool(Name = "update_relation_type"), Description("RelationTypeの名称と規則属性を更新します。")]
    public Task<OperationResult> UpdateRelationType(long relationTypeId, RelationTypeUpdate input, CancellationToken cancellationToken) => store.UpdateRelationTypeAsync(relationTypeId, input, cancellationToken);

    [McpServerTool(Name = "delete_relation_type"), Description("未使用のRelationTypeを物理削除します。")]
    public Task<OperationResult> DeleteRelationType(long relationTypeId, CancellationToken cancellationToken) => store.DeleteRelationTypeAsync(relationTypeId, cancellationToken);

    [McpServerTool(Name = "create_event"), Description("actor/occurred_at/action/objectを持つEventを登録します。合成はcontains/part_of Relationで表現します。")]
    public Task<EventRecord> CreateEvent(EventInput input, CancellationToken cancellationToken) => store.CreateEventAsync(input, cancellationToken);
}
