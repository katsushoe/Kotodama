using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
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
    private const string HttpToken = "integration-test-token";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"kotodama-http-{Guid.NewGuid():N}.db");
    private Uri _endpoint = null!;
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
        startInfo.Environment["KOTODAMA_HTTP_TOKEN"] = HttpToken;
        _server = Process.Start(startInfo) ?? throw new InvalidOperationException("Kotodama HTTP test server could not start.");
        _endpoint = new Uri($"http://127.0.0.1:{port}{ServerSettings.HttpPath}");
        _client = await ConnectAsync(_endpoint, HttpToken);
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
        await DeleteDatabaseFilesAsync(_databasePath);
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

        GetResponseJson(result).Should().Contain("Kotodama").And.Contain("0.11.1");
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

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-token")]
    public async Task McpEndpoint_WhenBearerTokenIsInvalid_ReturnsUnauthorized(string? authorization)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { } }),
        };
        if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle(value => value.Scheme == "Bearer");
    }

    [Fact]
    public async Task McpEndpoint_WhenBearerTokenIsNotConfigured_AllowsLoopbackClient()
    {
        var port = GetAvailablePort();
        var databasePath = Path.Combine(Path.GetTempPath(), $"kotodama-http-no-auth-{Guid.NewGuid():N}.db");
        var serverAssembly = typeof(KnowledgeStore).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet", $"\"{serverAssembly}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(serverAssembly),
        };
        startInfo.Environment["KOTODAMA_DB"] = databasePath;
        startInfo.Environment["KOTODAMA_TRANSPORT"] = "http";
        startInfo.Environment["KOTODAMA_HTTP_URL"] = $"http://127.0.0.1:{port}";
        using var server = Process.Start(startInfo) ?? throw new InvalidOperationException("Kotodama HTTP test server could not start.");

        try
        {
            await using var client = await ConnectAsync(new Uri($"http://127.0.0.1:{port}{ServerSettings.HttpPath}"), null);

            client.ServerCapabilities.Tools.Should().NotBeNull();
        }
        finally
        {
            if (!server.HasExited)
            {
                server.Kill(true);
                await server.WaitForExitAsync();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await DeleteDatabaseFilesAsync(databasePath);
        }
    }

    private static async Task<McpClient> ConnectAsync(Uri endpoint, string? httpToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var options = new HttpClientTransportOptions
                {
                    Endpoint = endpoint,
                    TransportMode = HttpTransportMode.StreamableHttp,
                    ConnectionTimeout = TimeSpan.FromSeconds(5),
                };
                if (httpToken is not null)
                {
                    options.AdditionalHeaders = new Dictionary<string, string>
                    {
                        ["Authorization"] = $"Bearer {httpToken}",
                    };
                }

                var transport = new HttpClientTransport(options);
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

    private static async Task DeleteDatabaseFilesAsync(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
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
