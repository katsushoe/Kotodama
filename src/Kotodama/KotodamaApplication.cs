using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Kotodama;

/// <summary>KotodamaのMCP Hostを構成して実行します。</summary>
internal static class KotodamaApplication
{
    /// <summary>設定されたTransportでKotodamaを実行します。</summary>
    internal static Task<int> RunAsync(string[] args)
    {
        if (args.SequenceEqual(["configure", "codex"], StringComparer.OrdinalIgnoreCase))
        {
            return UserIntegration.ConfigureCodexAsync(AppContext.BaseDirectory);
        }

        if (args.SequenceEqual(["unconfigure", "codex"], StringComparer.OrdinalIgnoreCase))
        {
            return UserIntegration.UnconfigureCodexAsync();
        }

        if (args.SequenceEqual(["configure", "claude"], StringComparer.OrdinalIgnoreCase))
        {
            return ClaudeIntegration.ConfigureAsync(AppContext.BaseDirectory);
        }

        if (args.SequenceEqual(["unconfigure", "claude"], StringComparer.OrdinalIgnoreCase))
        {
            return ClaudeIntegration.UnconfigureAsync();
        }

        if (args.SequenceEqual(["configure", "all"], StringComparer.OrdinalIgnoreCase))
        {
            return UserIntegration.ConfigureAllAsync(AppContext.BaseDirectory);
        }

        if (args.SequenceEqual(["unconfigure", "all"], StringComparer.OrdinalIgnoreCase))
        {
            return UserIntegration.UnconfigureAllAsync();
        }

        if (args.Length >= 3 &&
            args[0].Equals("hook", StringComparison.OrdinalIgnoreCase) &&
            args[1].Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            return ClaudeHookCommand.RunAsync(args[2], Console.In, Console.Out);
        }

        var settings = args.Contains("--http", StringComparer.OrdinalIgnoreCase)
            ? ServerSettings.Parse("http", ServerSettings.DefaultHttpUrl)
            : ServerSettings.FromEnvironment();
        return settings.Transport == McpTransport.Http ? RunHttpAsync(args, settings) : RunStdioAsync(args);
    }

    private static async Task<int> RunStdioAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureLogging(builder.Logging);
        AddCoreServices(builder.Services);
        AddMcpPrimitives(builder.Services.AddMcpServer().WithStdioServerTransport());

        using var host = builder.Build();
        await InitializeStoreAsync(host.Services);
        await host.RunAsync();
        return 0;
    }

    private static async Task<int> RunHttpAsync(string[] args, ServerSettings settings)
    {
        var httpUrl = settings.HttpUrl ?? throw new InvalidOperationException("HTTP transport requires KOTODAMA_HTTP_URL.");
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(httpUrl.AbsoluteUri.TrimEnd('/'));
        ConfigureLogging(builder.Logging);
        AddCoreServices(builder.Services);
        AddMcpPrimitives(builder.Services.AddMcpServer().WithHttpTransport(options => options.Stateless = true));

        await using var app = builder.Build();
        app.MapMcp(ServerSettings.HttpPath);
        await InitializeStoreAsync(app.Services);
        await app.RunAsync();
        return 0;
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        var databasePath = Environment.GetEnvironmentVariable("KOTODAMA_DB") ?? ApplicationPaths.GetDefaultDatabasePath(AppContext.BaseDirectory);
        var tempStoreText = Environment.GetEnvironmentVariable("KOTODAMA_DREAM_TEMP_STORE");
        var tempStore = Enum.TryParse<DreamTempStore>(tempStoreText, true, out var parsedTempStore) ? parsedTempStore : DreamTempStore.Default;
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new KnowledgeStore(databasePath, provider.GetRequiredService<TimeProvider>(), tempStore));
        services.Configure<McpServerOptions>(options => options.ServerInstructions = KotodamaGuidance.ServerInstructions);
    }

    private static void AddMcpPrimitives(IMcpServerBuilder builder) =>
        builder.WithTools<KotodamaTools>().WithPrompts<KotodamaPrompts>();

    private static void ConfigureLogging(ILoggingBuilder logging) =>
        logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    private static Task InitializeStoreAsync(IServiceProvider services) =>
        services.GetRequiredService<KnowledgeStore>().InitializeAsync();
}
