using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Kotodama.Tests;

/// <summary>統合テスト用に一時DBでKotodama HTTPサーバーを起動します。</summary>
internal sealed class KotodamaHttpTestServer : IAsyncDisposable
{
    private readonly Process _process;

    private KotodamaHttpTestServer(Process process, string databasePath, Uri endpoint)
    {
        _process = process;
        DatabasePath = databasePath;
        Endpoint = endpoint;
    }

    internal string DatabasePath { get; }

    internal Uri Endpoint { get; }

    internal static KotodamaHttpTestServer Start(string? httpToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        var port = GetAvailablePort();
        var databasePath = Path.Combine(Path.GetTempPath(), $"kotodama-http-{Guid.NewGuid():N}.db");
        var serverAssembly = typeof(KnowledgeStore).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet", $"\"{serverAssembly}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(serverAssembly),
        };
        startInfo.Environment["KOTODAMA_DB"] = databasePath;
        startInfo.Environment["KOTODAMA_DREAM_TEMP_STORE"] = "Memory";
        startInfo.Environment["KOTODAMA_HTTP_URL"] = $"http://127.0.0.1:{port}";
        startInfo.Environment.Remove("KOTODAMA_TRANSPORT");
        if (httpToken is null) startInfo.Environment.Remove("KOTODAMA_HTTP_TOKEN");
        else startInfo.Environment["KOTODAMA_HTTP_TOKEN"] = httpToken;
        foreach (var (key, value) in environment ?? new Dictionary<string, string>()) startInfo.Environment[key] = value;

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Kotodama HTTP test server could not start.");
        return new(process, databasePath, new Uri($"http://127.0.0.1:{port}{ServerSettings.HttpPath}"));
    }

    internal async Task<McpClient> ConnectAsync(string? httpToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                var options = new HttpClientTransportOptions
                {
                    Endpoint = Endpoint,
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

                return await McpClient.CreateAsync(new HttpClientTransport(options), cancellationToken: CancellationToken.None);
            }
            catch (Exception exception) when (exception is HttpRequestException or McpException)
            {
                lastException = exception;
                await Task.Delay(250);
            }
        }

        throw new InvalidOperationException("Kotodama HTTP test server did not become ready.", lastException);
    }

    internal async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken);
        return _process.ExitCode;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" })
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

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
