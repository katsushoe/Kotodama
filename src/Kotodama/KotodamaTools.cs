using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Kotodama;

/// <summary>Kotodama MCP Tool 群です。</summary>
[McpServerToolType]
public sealed class KotodamaTools(KnowledgeStore store)
{
    [McpServerTool(Name = "create_tag"), Description("namespace内の正規化タグを作成・再利用します。名前はNFKC・前後空白除去・Invariant大文字で照合します。")]
    public Task<TagRecord> CreateTag(string name, string entityNamespace = "global", CancellationToken cancellationToken = default) => ValidateToolAsync(() => store.CreateTagAsync(name, entityNamespace, cancellationToken));

    [McpServerTool(Name = "list_tags", ReadOnly = true), Description("namespace内のタグをID順で返します。統合済みIDとmergedIntoIdも含みます。")]
    public Task<IReadOnlyList<TagRecord>> ListTags(string entityNamespace = "global", long afterId = 0, int limit = 50, CancellationToken cancellationToken = default) => ValidateToolAsync(() => store.ListTagsAsync(entityNamespace, afterId, limit, cancellationToken));

    [McpServerTool(Name = "rename_tag"), Description("タグをIDで改名し旧名を別名として保持します。別タグの名前と衝突する場合は拒否します。")]
    public Task<TagRecord> RenameTag(long tagId, string name, string entityNamespace = "global", CancellationToken cancellationToken = default) => ValidateToolAsync(() => store.RenameTagAsync(tagId, name, entityNamespace, cancellationToken));

    [McpServerTool(Name = "add_tag_alias"), Description("タグIDへ別名を追加します。namespace内で正規名と同じ一意性を適用します。")]
    public Task<TagRecord> AddTagAlias(long tagId, string alias, string entityNamespace = "global", CancellationToken cancellationToken = default) => ValidateToolAsync(() => store.AddTagAliasAsync(tagId, alias, entityNamespace, cancellationToken));

    [McpServerTool(Name = "merge_tags"), Description("明示された同一namespaceのタグを統合します。関連・別名を移し、旧IDは統合先へ解決します。")]
    public Task<TagRecord> MergeTags(long sourceTagId, long targetTagId, string entityNamespace = "global", CancellationToken cancellationToken = default) => ValidateToolAsync(() => store.MergeTagsAsync(sourceTagId, targetTagId, entityNamespace, cancellationToken));

    [McpServerTool(Name = "set_knowledge_tags"), Description("input/claimへタグIDを付与・解除します。targetIdsまたはknowledgeSubjectIdを指定。dryRun既定true、実行はexpectedCount必須で件数相違時に拒否します。後付け・解除は指定対象のみに適用します。")]
    public Task<TagUpdateResult> SetKnowledgeTags(SetKnowledgeTagsInput input, CancellationToken cancellationToken) => ValidateToolAsync(() => store.SetKnowledgeTagsAsync(input, cancellationToken));

    [McpServerTool(Name = "query_tagged_inputs", ReadOnly = true), Description("本文を持たない知識入力をtags/tagIdsの完全一致で検索します。tagMatch=any/all、namespace、afterId、limitを指定し、語彙と付与由来を返します。")]
    public Task<IReadOnlyList<TaggedKnowledgeInput>> QueryTaggedInputs(TagQueryInput input, CancellationToken cancellationToken) => ValidateToolAsync(() => store.QueryTaggedInputsAsync(input, cancellationToken));

    [McpServerTool(Name = "query_tagged_statements", ReadOnly = true), Description("Protocol 1互換エラーを返します。query_tagged_inputsへ移行してください。")]
    public static OperationResult QueryTaggedStatements(TagQueryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(false, "protocol_incompatible", "query_tagged_statements was removed in protocol 2; use query_tagged_inputs.");
    }

    [McpServerTool(Name = "query_tagged_claims", ReadOnly = true), Description("Claimをtags/tagIdsの完全一致で検索します。tagMatch=any/all、namespace、状態、有効時点、afterId、limitを指定し、付与由来を返します。")]
    public Task<IReadOnlyList<TaggedClaim>> QueryTaggedClaims(TagQueryInput input, CancellationToken cancellationToken) => ValidateToolAsync(() => store.QueryTaggedClaimsAsync(input, cancellationToken));

    private static async Task<T> ValidateToolAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ArgumentException error)
        {
            // 仕様上の入力エラーをToolエラーへ変換し、DB障害・キャンセルは伝播します。
            throw new McpException(error.Message, error);
        }
    }

    [McpServerTool(Name = "get_version"), Description("稼働中のKotodamaバージョンを返します。")]
    public static object GetVersion() => new { name = "Kotodama", version = "0.17.0", protocolVersion = 2, schemaVersion = 2 };

    [McpServerTool(Name = "get_entity"), Description("IDでEntityを取得します。存在しない場合はnullです。")]
    public Task<EntityRecord?> GetEntity(long id, CancellationToken cancellationToken) => store.GetEntityAsync(id, cancellationToken);

    [McpServerTool(Name = "get_knowledge_input", ReadOnly = true), Description("IDで本文を持たない知識入力と順序を持たない語彙を取得します。存在しない場合はnullです。")]
    public Task<KnowledgeInputRecord?> GetKnowledgeInput(long id, CancellationToken cancellationToken) => store.GetKnowledgeInputAsync(id, cancellationToken);

    [McpServerTool(Name = "get_statement", ReadOnly = true), Description("Protocol 1互換エラーを返します。get_knowledge_inputへ移行してください。")]
    public static OperationResult GetStatement(long id) => new(false, "protocol_incompatible", "get_statement was removed in protocol 2; use get_knowledge_input.", id);

    [McpServerTool(Name = "search_entities"), Description("名前の部分一致を優先し、同じnamespaceの有効なPositive similar_to/equalsとSimilarityGroup所属を辿った関連候補を合計limit件まで返します。matchは到達理由とClaim経路です。related_pathは類似性の推移を意味しません。includeRelated=falseで名前一致のみです。")]
    public Task<IReadOnlyList<EntityRecord>> SearchEntities(string query, int limit = 50, bool includeRelated = true, CancellationToken cancellationToken = default) => store.SearchEntitiesAsync(query, limit, cancellationToken, includeRelated);

    [McpServerTool(Name = "get_equivalent_entities", ReadOnly = true), Description("現在有効なPositive equals/canonical_ofの反射・対称・推移閉包を返します。自身を含み、撤回・stale・期限切れの辺は除外します。Entityの物理統合はしません。query_claimsは引き続き明示Claimのみを返します。")]
    public Task<IReadOnlyList<EntityRecord>> GetEquivalentEntities(long entityId, CancellationToken cancellationToken) => store.GetEquivalentEntitiesAsync(entityId, cancellationToken);

    [McpServerTool(Name = "merge_similarity_groups"), Description("明示的な統合依頼時だけ使用します。2つのSimilarityGroupから新規グループを作り、thresholdを各グループの有効な一意メンバー数で加重平均します。重複メンバーは新グループでは1件、両方空なら0.5です。旧所属Claimは撤回されます。自動統合・自動分裂はしません。")]
    public Task<SimilarityGroupResult> MergeSimilarityGroups(long groupAId, long groupBId, string? canonicalName = null, CancellationToken cancellationToken = default) => store.MergeSimilarityGroupsAsync(groupAId, groupBId, canonicalName, cancellationToken);

    [McpServerTool(Name = "propose_claim"), Description("Knowledge Candidateを規則検証し、妥当ならClaimとして保存します。")]
    public Task<OperationResult> ProposeClaim(ClaimCandidate candidate, CancellationToken cancellationToken) => store.ProposeClaimAsync(candidate, cancellationToken);

    [McpServerTool(Name = "remember_knowledge"), Description("Protocol 2。input.statementは解析中だけMemoryで扱い、DB、Log、応答へ保存しません。必須entities/relationsに原子的な概念と関係を渡します。語彙はNFKC正規化し、順序とOffsetを持たないinput_termsへ保存します。構造エラーは最大3回修正し、最終失敗も未保存です。結果のinputIdから非本文Metadataと語彙だけを参照できます。秘密情報らしい入力は保存前に拒否します。")]
    public Task<RememberKnowledgeResult> RememberKnowledge(StructuredKnowledgeInput input, CancellationToken cancellationToken) => ValidateToolAsync(() => store.RememberStructuredKnowledgeAsync(input, cancellationToken));

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

    [McpServerTool(Name = "create_entity"), Description("原子的なEntityを登録します。canonicalNameは1つの固有名・名詞・短い名詞句・識別子に限定し、文章とStatement/StatementRef classは拒否します。SimilarityGroupはmetadataに固定JSON文字列 {\"threshold\":0.5} を指定します。原文はEntityやmetadataへ保存できません。")]
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
