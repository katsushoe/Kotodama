using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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
        if (args.Length > 0 && args[0].Equals("call", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 3) throw new ArgumentException("Usage: Kotodama call <tool> <arguments.json>");
            var service = ServerSettings.Parse("http", Environment.GetEnvironmentVariable("KOTODAMA_HTTP_URL") ?? ServerSettings.DefaultHttpUrl,
                Environment.GetEnvironmentVariable("KOTODAMA_HTTP_TOKEN"));
            return ToolCallCommand.RunAsync(args[1], args[2], service, Console.Out);
        }

        if (args.SequenceEqual(["call-help"], StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Kotodama call <tool> <arguments.json>\nCalls any MCP tool on the running HTTP service. Arguments match the MCP schema.\nKOTODAMA_HTTP_URL sets the service URL; KOTODAMA_HTTP_TOKEN sets bearer authentication.\nExit code: 0 success, nonzero tool/input/connection error. Timeout: 60 seconds.\nTag tools: create_tag, list_tags, rename_tag, add_tag_alias, merge_tags, set_knowledge_tags, query_tagged_statements, query_tagged_claims.\nset_knowledge_tags defaults to dryRun=true; execution requires expectedCount.");
            return Task.FromResult(0);
        }

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
            (args[1].Equals("claude", StringComparison.OrdinalIgnoreCase) ||
             args[1].Equals("codex", StringComparison.OrdinalIgnoreCase)))
        {
            return ClaudeHookCommand.RunAsync(args[1], args[2], Console.In, Console.Out);
        }

        if (args.Length == 2 && args[0].Equals("backup", StringComparison.OrdinalIgnoreCase))
        {
            return BackupAsync(args[1]);
        }

        var settings = args.Contains("--http", StringComparer.OrdinalIgnoreCase)
            ? ServerSettings.Parse("http", ServerSettings.DefaultHttpUrl, Environment.GetEnvironmentVariable("KOTODAMA_HTTP_TOKEN"))
            : ServerSettings.FromEnvironment();
        return settings.Transport == McpTransport.Http ? RunHttpAsync(args, settings) : RunStdioAsync(args);
    }

    private static async Task<int> RunStdioAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureLogging(builder.Logging);
        AddCoreServices(builder.Services);
        builder.Services.AddHostedService<DreamWorker>();
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
        if (settings.HttpToken is not null)
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments(ServerSettings.HttpPath) &&
                    !HasValidBearerToken(context.Request.Headers.Authorization.ToString(), settings.HttpToken))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    return;
                }

                await next(context);
            });
        }

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

    private static void ConfigureLogging(ILoggingBuilder logging)
    {
        // 権限を要する既定EventLog出力でToolエラー応答自体が失敗しないよう、出力先を明示します。
        logging.ClearProviders();
        logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        logging.AddProvider(new DailyFileLoggerProvider(ApplicationPaths.GetLogDirectory(AppContext.BaseDirectory), TimeProvider.System));
    }

    private static async Task<int> BackupAsync(string destinationPath)
    {
        var databasePath = Environment.GetEnvironmentVariable("KOTODAMA_DB") ?? ApplicationPaths.GetDefaultDatabasePath(AppContext.BaseDirectory);
        var store = new KnowledgeStore(databasePath, TimeProvider.System);
        await store.InitializeAsync();
        await store.BackupAsync(destinationPath);
        Console.WriteLine(Path.GetFullPath(destinationPath));
        return 0;
    }

    private static Task InitializeStoreAsync(IServiceProvider services) =>
        services.GetRequiredService<KnowledgeStore>().InitializeAsync();

    private static bool HasValidBearerToken(string authorizationHeader, string expectedToken)
    {
        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization) ||
            !authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(authorization.Parameter))
        {
            return false;
        }

        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(authorization.Parameter));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedToken));
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
