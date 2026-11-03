using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Kotodama.Tests;

/// <summary>統合テスト用に一時DBでKotodama HTTPサーバーを起動します。</summary>
internal sealed class KotodamaHttpTestServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task<int> _runTask;
    private readonly string _settingsPath;

    private KotodamaHttpTestServer(string databasePath, string settingsPath, string logDirectory, KotodamaSettings settings, Uri endpoint)
    {
        DatabasePath = databasePath;
        _settingsPath = settingsPath;
        LogDirectory = logDirectory;
        Settings = settings;
        Endpoint = endpoint;
        _runTask = KotodamaApplication.RunHttpAsync([], settings, _shutdown.Token);
    }

    internal string DatabasePath { get; }

    internal Uri Endpoint { get; }

    internal string LogDirectory { get; }

    internal KotodamaSettings Settings { get; }

    internal static KotodamaHttpTestServer Start(string? httpToken)
    {
        var port = GetAvailablePort();
        var root = Path.Combine(Path.GetTempPath(), $"kotodama-http-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "kotodama.db");
        var settingsPath = Path.Combine(root, "kotodama.json");
        var logDirectory = Path.Combine(root, "logs");
        var httpUrl = $"http://127.0.0.1:{port}";
        var settings = new KotodamaSettings
        {
            DatabasePath = databasePath,
            LogDirectory = logDirectory,
            DreamTempStore = DreamTempStore.Memory,
            HttpUrl = httpUrl,
            HttpToken = httpToken,
            SourcePath = settingsPath,
        };
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
        {
            settings.DatabasePath,
            settings.LogDirectory,
            dreamTempStore = "Memory",
            settings.DreamIntervalSeconds,
            settings.Transport,
            settings.HttpUrl,
            settings.HttpToken,
        }));
        return new(databasePath, settingsPath, logDirectory, settings, new Uri($"{httpUrl}{ServerSettings.HttpPath}"));
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

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try
        {
            await _runTask.WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (OperationCanceledException) { }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm", _settingsPath })
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
        var root = Path.GetDirectoryName(DatabasePath)!;
        if (Directory.Exists(root)) Directory.Delete(root, true);
        _shutdown.Dispose();
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
