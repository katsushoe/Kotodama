using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

var logDirectory = Kotodama.ApplicationPaths.GetLogDirectory(AppContext.BaseDirectory);
using var loggerProvider = new Kotodama.DailyFileLoggerProvider(logDirectory, TimeProvider.System);
using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
using var exceptionHandler = new Kotodama.GlobalExceptionHandler(loggerFactory.CreateLogger("Kotodama.HealthCheck.Process"));
exceptionHandler.Register();
return await exceptionHandler.RunAsync(RunHealthCheckAsync);

static async Task<int> RunHealthCheckAsync()
{
    var endpoint = new Uri(Kotodama.ServerSettings.DefaultHttpUrl + Kotodama.ServerSettings.HttpPath);

    static async Task<T> MeasureAsync<T>(string name, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await action();
        stopwatch.Stop();
        Console.WriteLine($"{name}_MS={stopwatch.Elapsed.TotalMilliseconds:F1}");
        return result;
    }

    using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    {
        var response = await MeasureAsync("HTTP_GET", () => httpClient.GetAsync(endpoint));
        Console.WriteLine($"HTTP_GET_STATUS={(int)response.StatusCode}");
    }

    var options = new HttpClientTransportOptions
    {
        Endpoint = endpoint,
        TransportMode = HttpTransportMode.StreamableHttp,
        ConnectionTimeout = TimeSpan.FromSeconds(30),
    };

    await using var client = await MeasureAsync("MCP_INITIALIZE", async () =>
        await McpClient.CreateAsync(new HttpClientTransport(options)));
    Console.WriteLine($"SERVER={client.ServerInfo.Name} {client.ServerInfo.Version}");
    var tools = await MeasureAsync("LIST_TOOLS", async () => await client.ListToolsAsync());
    Console.WriteLine($"TOOLS={tools.Count}");
    var prompts = await MeasureAsync("LIST_PROMPTS", async () => await client.ListPromptsAsync());
    Console.WriteLine($"PROMPTS={prompts.Count}");

    var version = await MeasureAsync("GET_VERSION", async () => await client.CallToolAsync("get_version"));
    Console.WriteLine($"GET_VERSION={version.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text}");

    var entities = await MeasureAsync("SEARCH_ENTITIES", async () => await client.CallToolAsync(
        "search_entities",
        new Dictionary<string, object?> { ["query"] = "Kotodama health check no-match 20260830" }));
    Console.WriteLine($"SEARCH_ERROR={entities.IsError}");
    Console.WriteLine("HEALTHY=true");
    return 0;
}
