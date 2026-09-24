using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kotodama.Tests;

public sealed class Protocol2PersistenceTests
{
    [Fact]
    public void Extractor_NormalizesTermsWithoutOrderOrOffsets()
    {
        var terms = new DeterministicTermExtractor().Extract("Kotodama v0.16.0 API_client https://Example.COM/private/path Kotodama");

        terms.Should().Contain(x => x.CanonicalName == "kotodama" && x.OccurrenceCount == 2);
        terms.Should().Contain(x => x.CanonicalName == "v0.16.0");
        terms.Should().Contain(x => x.CanonicalName == "API_client");
        terms.Should().Contain(x => x.CanonicalName == "https://example.com");
    }

    [Fact]
    public async Task CreateEntity_RejectsDescriptiveSentenceLikeName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kotodama-atomic-{Guid.NewGuid():N}.db");
        try
        {
            var store = new KnowledgeStore(path, TimeProvider.System);
            await store.InitializeAsync();
            await store.Invoking(x => x.CreateEntityAsync(new("Claude DesktopでのUse Kotodama knowledge選択によるuse_kotodama_text添付")))
                .Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Remember_DoesNotPersistRawInputInDatabaseFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kotodama-protocol2-{Guid.NewGuid():N}.db");
        const string raw = "検査専用原文_7c9f3d は秘密の並び順を含みます。";
        try
        {
            var store = new KnowledgeStore(path, TimeProvider.System);
            await store.InitializeAsync();
            var result = await store.RememberStructuredKnowledgeAsync(new(raw,
                [new("product", "Kotodama"), new("feature", "語彙永続化")],
                [new("product", "feature", "member_of")], Reason: "persistence scan"));
            result.Ok.Should().BeTrue();
            (await store.GetKnowledgeInputAsync(result.InputId))!.Terms.Should().NotBeEmpty();

            SqliteConnection.ClearAllPools();
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }.Where(File.Exists))
            {
                var bytes = await File.ReadAllBytesAsync(candidate);
                Contains(bytes, Encoding.UTF8.GetBytes(raw)).Should().BeFalse(candidate);
                Contains(bytes, Encoding.Unicode.GetBytes(raw)).Should().BeFalse(candidate);
            }

            await using var db = new SqliteConnection($"Data Source={path}");
            await db.OpenAsync();
            await using var schema = db.CreateCommand();
            schema.CommandText = "SELECT sql FROM sqlite_master WHERE sql IS NOT NULL";
            var definitions = new StringBuilder();
            await using var reader = await schema.ExecuteReaderAsync();
            while (await reader.ReadAsync()) definitions.AppendLine(reader.GetString(0));
            definitions.ToString().Should().NotContain("statements").And.NotContain("source_statement_id");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task Remember_RejectsCredentialLikeInputBeforePersistence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kotodama-secret-{Guid.NewGuid():N}.db");
        try
        {
            var store = new KnowledgeStore(path, TimeProvider.System);
            await store.InitializeAsync();
            var result = await store.RememberStructuredKnowledgeAsync(new("api_key=abcdefghijklmnop",
                [new("a", "Kotodama"), new("b", "API")], [new("a", "b", "member_of")]));
            result.Ok.Should().BeFalse();
            (await store.SearchEntitiesAsync("")).Should().BeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Initialize_MigratesLegacyStatementAndSecurelyRemovesItsText(bool hasLegacyTags)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kotodama-migration-{Guid.NewGuid():N}.db");
        const string raw = "旧形式だけに存在する原文_31e6b0";
        try
        {
            await using (var db = new SqliteConnection($"Data Source={path}"))
            {
                await db.OpenAsync();
                await using var setup = db.CreateCommand();
                setup.CommandText = """
                    CREATE TABLE statements(id INTEGER PRIMARY KEY,text TEXT NOT NULL,namespace TEXT NOT NULL,created_at TEXT NOT NULL,updated_at TEXT NOT NULL);
                    CREATE TABLE statement_tags(statement_id INTEGER NOT NULL,tag_id INTEGER NOT NULL,origin TEXT NOT NULL);
                    CREATE TABLE sources(id INTEGER PRIMARY KEY,source_type TEXT NOT NULL,uri TEXT,external_id TEXT,title TEXT,author_entity_id INTEGER,source_reliability REAL,observed_at TEXT NOT NULL,metadata TEXT,source_statement_id INTEGER);
                    CREATE TABLE events(entity_id INTEGER PRIMARY KEY,actor_id INTEGER,occurred_at TEXT NOT NULL,action TEXT NOT NULL,object_id INTEGER,object_value TEXT,ends_at TEXT,source_statement_id INTEGER);
                    CREATE TABLE claim_tags(claim_id INTEGER NOT NULL,tag_id INTEGER NOT NULL,origin TEXT NOT NULL,source_statement_id INTEGER);
                    INSERT INTO statements VALUES(1,$raw,'global','2026-01-01T00:00:00.0000000+00:00','2026-01-01T00:00:00.0000000+00:00');
                    """;
                setup.Parameters.AddWithValue("$raw", raw);
                await setup.ExecuteNonQueryAsync();
                if (!hasLegacyTags)
                {
                    setup.CommandText = "DROP TABLE statement_tags; DROP TABLE claim_tags;";
                    await setup.ExecuteNonQueryAsync();
                }
                setup.CommandText = "PRAGMA journal_mode=WAL";
                setup.Parameters.Clear();
                (await setup.ExecuteScalarAsync()).Should().Be("wal");
            }

            var store = new KnowledgeStore(path, TimeProvider.System);
            await store.InitializeAsync();
            await store.InitializeAsync();
            (await store.GetKnowledgeInputAsync(1)).Should().NotBeNull();
            SqliteConnection.ClearAllPools();

            var bytes = await File.ReadAllBytesAsync(path);
            Contains(bytes, Encoding.UTF8.GetBytes(raw)).Should().BeFalse();
            Contains(bytes, Encoding.Unicode.GetBytes(raw)).Should().BeFalse();
            await using var migrated = new SqliteConnection($"Data Source={path}");
            await migrated.OpenAsync();
            await using var tables = migrated.CreateCommand();
            tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN('statements','statement_tags')";
            Convert.ToInt32(await tables.ExecuteScalarAsync()).Should().Be(0);
            tables.CommandText = "SELECT value FROM schema_metadata WHERE key='secure_compaction_completed'";
            (await tables.ExecuteScalarAsync()).Should().Be("1");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task Initialize_LegacyStatementsWithCurrentReferences_PreservesKnowledgeAcrossRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kotodama-mixed-migration-{Guid.NewGuid():N}.db");
        try
        {
            var store = new KnowledgeStore(path, TimeProvider.System);
            await store.InitializeAsync();
            var saved = await store.RememberStructuredKnowledgeAsync(new("Kotodama supports SQLite",
                [new("a", "Kotodama"), new("b", "SQLite")], [new("a", "b", "member_of")]));
            saved.Ok.Should().BeTrue();
            await using (var db = new SqliteConnection($"Data Source={path}"))
            {
                await db.OpenAsync();
                await using var setup = db.CreateCommand();
                setup.CommandText = """
                    CREATE TABLE statements(id INTEGER PRIMARY KEY,text TEXT NOT NULL,namespace TEXT NOT NULL,created_at TEXT NOT NULL,updated_at TEXT NOT NULL);
                    INSERT INTO statements VALUES(1000,'Legacy migration','global','2026-01-01T00:00:00.0000000+00:00','2026-01-01T00:00:00.0000000+00:00');
                    """;
                await setup.ExecuteNonQueryAsync();
            }

            // Restart releases pooled connections before changing the journal mode.
            SqliteConnection.ClearAllPools();
            await store.InitializeAsync();
            await store.InitializeAsync();

            (await store.GetKnowledgeInputAsync(saved.InputId)).Should().NotBeNull();
            (await store.GetKnowledgeInputAsync(1000))!.Terms.Should().NotBeEmpty();
            await using var check = new SqliteConnection($"Data Source={path}");
            await check.OpenAsync();
            await using var command = check.CreateCommand();
            command.CommandText = "PRAGMA foreign_key_check";
            (await command.ExecuteScalarAsync()).Should().BeNull();
            command.CommandText = "SELECT COUNT(*) FROM claims WHERE id=$id";
            command.Parameters.AddWithValue("$id", saved.ClaimIds[0]);
            Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) File.Delete(candidate);
        }
    }

    private static bool Contains(byte[] source, byte[] value) => source.AsSpan().IndexOf(value) >= 0;
}
