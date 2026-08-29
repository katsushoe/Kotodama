using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kotodama.Tests;

public sealed class McpHttpTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"kotodama-http-{Guid.NewGuid():N}.db");
    private McpClient _client = null!;
    private Process _server = null!;

    public async Task InitializeAsync()
    {
        var port = GetAvailablePort();
        var serverAssembly = typeof(KnowledgeStore).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet", $"\"{serverAssembly}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(serverAssembly),
        };
        startInfo.Environment["KOTODAMA_DB"] = _databasePath;
        startInfo.Environment["KOTODAMA_DREAM_TEMP_STORE"] = "Memory";
        startInfo.Environment["KOTODAMA_TRANSPORT"] = "http";
        startInfo.Environment["KOTODAMA_HTTP_URL"] = $"http://127.0.0.1:{port}";
        _server = Process.Start(startInfo) ?? throw new InvalidOperationException("Kotodama HTTP test server could not start.");
        _client = await ConnectAsync(new Uri($"http://127.0.0.1:{port}{ServerSettings.HttpPath}"));
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        if (_server is not null && !_server.HasExited)
        {
            _server.Kill(true);
            await _server.WaitForExitAsync();
        }

        _server?.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await DeleteDatabaseFilesAsync();
    }

    [Fact]
    public void Initialize_ThroughHttp_ReturnsInstructionsAndCapabilities()
    {
        _client.ServerCapabilities.Tools.Should().NotBeNull();
        _client.ServerCapabilities.Prompts.Should().NotBeNull();
        _client.ServerInstructions.Should().Contain("persistent structured knowledge");
    }

    [Fact]
    public async Task GetVersion_ThroughHttp_ReturnsServerIdentity()
    {
        var result = await _client.CallToolAsync("get_version", cancellationToken: CancellationToken.None);

        GetResponseJson(result).Should().Contain("Kotodama").And.Contain("0.9.0");
    }

    [Fact]
    public async Task CreateEntity_ThroughHttp_PersistsEntity()
    {
        var name = "HttpEntity" + Guid.NewGuid().ToString("N");
        await _client.CallToolAsync("create_entity", new Dictionary<string, object?> { ["input"] = new { canonicalName = name } });
        var result = await _client.CallToolAsync("search_entities", new Dictionary<string, object?> { ["query"] = name });

        GetResponseJson(result).Should().Contain(name);
    }

    [Fact]
    public async Task ProposeClaim_ThroughHttp_WhenReferencesAreMissing_ReturnsBusinessError()
    {
        var result = await _client.CallToolAsync("propose_claim", new Dictionary<string, object?>
        {
            ["candidate"] = new { subjectId = long.MaxValue, objectId = long.MaxValue - 1, relationType = "missing" },
        });

        result.IsError.Should().NotBeTrue();
        GetResponseJson(result).Should().Contain("rejected");
    }

    private static async Task<McpClient> ConnectAsync(Uri endpoint)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var transport = new HttpClientTransport(new()
                {
                    Endpoint = endpoint,
                    TransportMode = HttpTransportMode.StreamableHttp,
                    ConnectionTimeout = TimeSpan.FromSeconds(5),
                });
                return await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
            }
            catch (Exception exception) when (exception is HttpRequestException or McpException)
            {
                lastException = exception;
                await Task.Delay(250);
            }
        }

        throw new InvalidOperationException("Kotodama HTTP test server did not become ready.", lastException);
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string GetResponseJson(CallToolResult result)
    {
        if (result.StructuredContent is not null) return JsonSerializer.Serialize(result.StructuredContent);
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }

    private async Task DeleteDatabaseFilesAsync()
    {
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    break;
                }
                catch (IOException) when (attempt < 19)
                {
                    await Task.Delay(100);
                }
            }
        }
    }
}
