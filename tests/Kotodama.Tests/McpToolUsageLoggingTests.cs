using System.Globalization;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Client;
using Xunit;

namespace Kotodama.Tests;

public sealed class McpToolUsageLoggingTests : IAsyncLifetime
{
    private KotodamaHttpTestServer _server = null!;
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        _server = KotodamaHttpTestServer.Start(null);
        _client = await _server.ConnectAsync(null);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        if (_server is not null) await _server.DisposeAsync();
    }

    [Fact]
    public async Task CallTool_ThroughHttp_RecordsUsageMetadataWithoutArguments()
    {
        const string privateMarker = "private-query-" + "usage-log-test";

        var result = await _client.CallToolAsync("search_entities", new Dictionary<string, object?>
        {
            ["query"] = privateMarker,
        });

        Assert.NotEqual(true, result.IsError);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _server.DatabasePath }.ToString());
        await connection.OpenAsync();

        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT tool_name,started_at,duration_ms,outcome FROM mcp_tool_usage_log ORDER BY id DESC LIMIT 1";
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("search_entities", reader.GetString(0));
            Assert.True(DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
            Assert.True(reader.GetInt64(2) >= 0);
            Assert.Equal("success", reader.GetString(3));
            Assert.False(await reader.ReadAsync());
        }

        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "PRAGMA table_info(mcp_tool_usage_log)";
            await using var reader = await schema.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            Assert.Equal(["id", "tool_name", "started_at", "duration_ms", "outcome"], columns);
            Assert.DoesNotContain(privateMarker, string.Join('|', columns));
        }
    }

    [Fact]
    public async Task CallTool_WhenInputFails_RecordsErrorOutcome()
    {
        var result = await _client.CallToolAsync("get_entity", new Dictionary<string, object?>
        {
            ["id"] = "not-an-integer",
        });

        Assert.True(result.IsError);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _server.DatabasePath }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT tool_name,outcome FROM mcp_tool_usage_log ORDER BY id DESC LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("get_entity", reader.GetString(0));
        Assert.Equal("error", reader.GetString(1));
    }

    [Fact]
    public async Task Initialize_WhenDatabasePredatesUsageLog_CreatesUsageTable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kotodama-usage-migration-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE schema_metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL)";
                await command.ExecuteNonQueryAsync();
            }

            var store = new KnowledgeStore(path, TimeProvider.System);
            await store.InitializeAsync();

            await using var migrated = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await migrated.OpenAsync();
            await using var check = migrated.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='mcp_tool_usage_log'";
            Assert.Equal(1L, (long)(await check.ExecuteScalarAsync())!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }
}
