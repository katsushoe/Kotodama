using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Kotodama;

public sealed partial class KnowledgeStore
{
    private const string ClaimSearchColumns = """
        claim_id,relation_id,relation_type_id,relation_type,relation_kind,
        subject_id,subject_class_name,subject_name,subject_namespace,
        object_id,object_class_name,object_name,object_namespace,
        polarity,claim_confidence,attribution_confidence,strength,knowledge_subject_id,
        source_id,source_title,assertion_type,observed_at,valid_from,valid_to,last_confirmed_at,
        status,source_input_id
        """;

    private const string ClaimSearchSelect = """
        SELECT c.id,r.id,rt.id,rt.canonical_name,r.relation_kind,
               a.id,a.class_name,a.canonical_name,a.namespace,
               b.id,b.class_name,b.canonical_name,b.namespace,
               c.polarity,c.claim_confidence,c.attribution_confidence,c.strength,c.knowledge_subject_id,
               c.source_id,src.title,c.assertion_type,c.observed_at,c.valid_from,c.valid_to,c.last_confirmed_at,
               c.status,src.source_input_id
        FROM claims c
        JOIN relations r ON r.id=c.relation_id
        JOIN relation_types rt ON rt.id=r.relation_type_id
        LEFT JOIN directed_relations d ON d.relation_id=r.id
        LEFT JOIN symmetric_relations s ON s.relation_id=r.id
        JOIN entities a ON a.id=COALESCE(d.subject_id,s.entity_a_id)
        JOIN entities b ON b.id=COALESCE(d.object_id,s.entity_b_id)
        LEFT JOIN sources src ON src.id=c.source_id
        """;

    private async Task InitializeClaimSearchAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $$"""
            CREATE TABLE IF NOT EXISTS claim_search(
                claim_id INTEGER PRIMARY KEY REFERENCES claims(id) ON DELETE CASCADE,
                relation_id INTEGER NOT NULL,
                relation_type_id INTEGER NOT NULL,
                relation_type TEXT NOT NULL,
                relation_kind TEXT NOT NULL CHECK(relation_kind IN('directed','symmetric')),
                subject_id INTEGER NOT NULL,
                subject_class_name TEXT NOT NULL,
                subject_name TEXT NOT NULL,
                subject_namespace TEXT NOT NULL,
                object_id INTEGER NOT NULL,
                object_class_name TEXT NOT NULL,
                object_name TEXT NOT NULL,
                object_namespace TEXT NOT NULL,
                polarity TEXT NOT NULL CHECK(polarity IN('positive','negative')),
                claim_confidence REAL NOT NULL CHECK(claim_confidence BETWEEN 0 AND 1),
                attribution_confidence REAL,
                strength REAL,
                knowledge_subject_id INTEGER,
                source_id INTEGER,
                source_title TEXT,
                assertion_type TEXT NOT NULL,
                observed_at TEXT NOT NULL,
                valid_from TEXT,
                valid_to TEXT,
                last_confirmed_at TEXT,
                status TEXT NOT NULL CHECK(status IN('active','retracted','stale')),
                source_input_id INTEGER);
            CREATE INDEX IF NOT EXISTS idx_claim_search_subject ON claim_search(subject_id,status,claim_id);
            CREATE INDEX IF NOT EXISTS idx_claim_search_object ON claim_search(object_id,status,claim_id);
            CREATE INDEX IF NOT EXISTS idx_claim_search_type ON claim_search(relation_type,status,claim_id);
            CREATE INDEX IF NOT EXISTS idx_claim_search_namespace ON claim_search(subject_namespace,object_namespace,claim_id);
            CREATE INDEX IF NOT EXISTS idx_claim_search_temporal ON claim_search(valid_from,valid_to);

            CREATE TRIGGER IF NOT EXISTS trg_claim_search_insert AFTER INSERT ON claims BEGIN
                INSERT OR REPLACE INTO claim_search({{ClaimSearchColumns}})
                {{ClaimSearchSelect}} WHERE c.id=NEW.id;
            END;
            CREATE TRIGGER IF NOT EXISTS trg_claim_search_update AFTER UPDATE ON claims BEGIN
                INSERT OR REPLACE INTO claim_search({{ClaimSearchColumns}})
                {{ClaimSearchSelect}} WHERE c.id=NEW.id;
            END;
            CREATE TRIGGER IF NOT EXISTS trg_claim_search_delete AFTER DELETE ON claims BEGIN
                DELETE FROM claim_search WHERE claim_id=OLD.id;
            END;
            CREATE TRIGGER IF NOT EXISTS trg_claim_search_entity_update AFTER UPDATE OF class_name,canonical_name,namespace ON entities BEGIN
                UPDATE claim_search
                SET subject_class_name=NEW.class_name,subject_name=NEW.canonical_name,subject_namespace=NEW.namespace
                WHERE subject_id=NEW.id;
                UPDATE claim_search
                SET object_class_name=NEW.class_name,object_name=NEW.canonical_name,object_namespace=NEW.namespace
                WHERE object_id=NEW.id;
            END;
            CREATE TRIGGER IF NOT EXISTS trg_claim_search_relation_type_update AFTER UPDATE OF canonical_name ON relation_types BEGIN
                UPDATE claim_search SET relation_type=NEW.canonical_name WHERE relation_type_id=NEW.id;
            END;
            CREATE TRIGGER IF NOT EXISTS trg_claim_search_source_update AFTER UPDATE OF title,source_input_id ON sources BEGIN
                UPDATE claim_search SET source_title=NEW.title,source_input_id=NEW.source_input_id WHERE source_id=NEW.id;
            END;

            INSERT OR REPLACE INTO claim_search({{ClaimSearchColumns}})
            {{ClaimSearchSelect}}
            WHERE c.id NOT IN(SELECT claim_id FROM claim_search);
            INSERT INTO schema_metadata(key,value) VALUES('claim_search_version','1')
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;
            """;
        await command.ExecuteNonQueryAsync(token);

        command.CommandText = "SELECT COUNT(*) FROM claims WHERE id NOT IN(SELECT claim_id FROM claim_search)";
        var missing = Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        if (missing != 0) throw new InvalidOperationException("claim_search projection is incomplete.");
        await transaction.CommitAsync(token);
    }
}
