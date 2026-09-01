using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Kotodama;

/// <summary>Kotodama MCP Tool 群です。</summary>
[McpServerToolType]
public sealed class KotodamaTools(KnowledgeStore store)
{
    [McpServerTool(Name = "get_version"), Description("稼働中のKotodamaバージョンを返します。")]
    public static object GetVersion() => new { name = "Kotodama", version = "0.11.5" };

    [McpServerTool(Name = "get_entity"), Description("IDでEntityを取得します。存在しない場合はnullです。")]
    public Task<EntityRecord?> GetEntity(long id, CancellationToken cancellationToken) => store.GetEntityAsync(id, cancellationToken);

    [McpServerTool(Name = "search_entities"), Description("canonical nameの部分一致でEntityを検索します。")]
    public Task<IReadOnlyList<EntityRecord>> SearchEntities(string query, int limit = 50, CancellationToken cancellationToken = default) => store.SearchEntitiesAsync(query, limit, cancellationToken);

    [McpServerTool(Name = "propose_claim"), Description("Knowledge Candidateを規則検証し、妥当ならClaimとして保存します。")]
    public Task<OperationResult> ProposeClaim(ClaimCandidate candidate, CancellationToken cancellationToken) => store.ProposeClaimAsync(candidate, cancellationToken);

    [McpServerTool(Name = "remember_knowledge"), Description("ユーザーが『覚えて』『記憶して』『今後参照して』と依頼した自然文を、組み込みメモリの代わりにKotodamaへ永続保存します。textへユーザーの事実を改変せず渡してください。完全一致するActive Claimは重複登録しません。")]
    public Task<RememberKnowledgeResult> RememberKnowledge(RememberKnowledgeInput input, CancellationToken cancellationToken) => store.RememberKnowledgeAsync(input, cancellationToken);

    [McpServerTool(Name = "retract_claim"), Description("Claimを物理削除せずretractedへ変更します。")]
    public Task<OperationResult> RetractClaim(long claimId, CancellationToken cancellationToken) => store.RetractClaimAsync(claimId, cancellationToken);

    [McpServerTool(Name = "reactivate_claim"), Description("撤回またはstaleのClaimを再確認済みのactiveへ戻します。")]
    public Task<OperationResult> ReactivateClaim(long claimId, DateTimeOffset? confirmedAt = null, CancellationToken cancellationToken = default) => store.ReactivateClaimAsync(claimId, confirmedAt, cancellationToken);

    [McpServerTool(Name = "delete_claim"), Description("指定Claimを物理削除します。取り消せません。")]
    public Task<OperationResult> DeleteClaim(long claimId, CancellationToken cancellationToken) => store.DeleteClaimAsync(claimId, cancellationToken);

    [McpServerTool(Name = "query_claims"), Description("ClaimをEntity、RelationType、過去時点で検索します。空配列はunknownです。")]
    public Task<IReadOnlyList<ClaimRecord>> QueryClaims(long? entityId = null, string? relationType = null, DateTimeOffset? validAt = null, bool includeRetracted = false, CancellationToken cancellationToken = default) => store.QueryClaimsAsync(entityId, relationType, validAt, includeRetracted, cancellationToken);

    [McpServerTool(Name = "query_relations"), Description("Relationと関連Claimを検索します。")]
    public Task<IReadOnlyList<ClaimRecord>> QueryRelations(long? entityId = null, string? relationType = null, CancellationToken cancellationToken = default) => store.QueryClaimsAsync(entityId, relationType, cancellationToken: cancellationToken);

    [McpServerTool(Name = "get_neighbors"), Description("Entityに接続する有向・対称Relationを返します。")]
    public Task<IReadOnlyList<ClaimRecord>> GetNeighbors(long entityId, CancellationToken cancellationToken) => store.QueryClaimsAsync(entityId, cancellationToken: cancellationToken);

    [McpServerTool(Name = "get_knowledge_context"), Description("Entityの現在有効なKnowledge Contextを返します。")]
    public Task<IReadOnlyList<ClaimRecord>> GetKnowledgeContext(long entityId, CancellationToken cancellationToken) => store.QueryClaimsAsync(entityId, validAt: DateTimeOffset.UtcNow, cancellationToken: cancellationToken);

    [McpServerTool(Name = "run_dream"), Description("期限超過したClaimをfalseにせずstaleへ変更します。")]
    public Task<DreamResult> RunDream(CancellationToken cancellationToken) => store.RunDreamAsync(cancellationToken);

    [McpServerTool(Name = "create_entity"), Description("Entityを登録します。")]
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
