using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Kotodama;

/// <summary>KotodamaのMCP Hostを構成して実行します。</summary>
internal static class KotodamaApplication
{
    /// <summary>MSIが登録するWindowsサービス名です。</summary>
    internal const string WindowsServiceName = "Kotodama";

    /// <summary>CLIサブコマンド、またはStreamable HTTPサーバーとしてKotodamaを実行します。</summary>
    internal static Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("call", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 3) throw new ArgumentException("Usage: Kotodama call <tool> <arguments.json>");
            var service = KotodamaSettings.Load(KotodamaSettings.ConfigurationPath).GetServerSettings();
            return ToolCallCommand.RunAsync(args[1], args[2], service, Console.Out);
        }

        if (args.SequenceEqual(["call-help"], StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Kotodama call <tool> <arguments.json>\nCalls any MCP tool on the running HTTP service. Arguments match the MCP schema.\nThe server URL and bearer token come from C:\\Kotodama\\config\\kotodama.json.\nExit code: 0 success, nonzero tool/input/connection error. Timeout: 60 seconds.\nTag tools: create_tag, list_tags, rename_tag, add_tag_alias, merge_tags, set_knowledge_tags, query_tagged_inputs, query_tagged_claims.\nset_knowledge_tags defaults to dryRun=true; execution requires expectedCount.");
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
            return RunHookAsync(args[1], args[2]);
        }

        if (args.Length == 2 && args[0].Equals("backup", StringComparison.OrdinalIgnoreCase))
        {
            return BackupAsync(args[1], KotodamaSettings.Load(KotodamaSettings.ConfigurationPath));
        }

        return RunHttpAsync(args, KotodamaSettings.Load(KotodamaSettings.ConfigurationPath));
    }

    private static async Task<int> RunHookAsync(string clientName, string eventName)
    {
        await using var input = Console.OpenStandardInput();
        return await ClaudeHookCommand.RunAsync(clientName, eventName, input, Console.Out);
    }

    internal static async Task<int> RunHttpAsync(string[] args, KotodamaSettings settings, CancellationToken cancellationToken = default, Action? started = null)
    {
        // Windowsサービスとして起動された場合だけSCMと連携し、Content Rootを実行ファイルの場所へ固定します。
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(settings.SourcePath, optional: false, reloadOnChange: false);
        builder.Host.UseWindowsService(options => options.ServiceName = WindowsServiceName);
        var serverSettings = settings.GetServerSettings();
        builder.WebHost.UseUrls(serverSettings.HttpUrl.AbsoluteUri.TrimEnd('/'));
        ConfigureLogging(builder.Logging, settings.LogDirectory);
        AddCoreServices(builder.Services, settings);
        builder.Services.AddSingleton(settings);
        builder.Services.AddHostedService<DreamWorker>();
        AddMcpPrimitives(builder.Services.AddMcpServer().WithHttpTransport(options => options.Stateless = true));

        await using var app = builder.Build();
        using var startedRegistration = app.Lifetime.ApplicationStarted.Register(() => started?.Invoke());
        if (serverSettings.HttpToken is not null)
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments(ServerSettings.HttpPath) &&
                    !HasValidBearerToken(context.Request.Headers.Authorization.ToString(), serverSettings.HttpToken))
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
        await app.RunAsync(cancellationToken);
        return 0;
    }

    private static void AddCoreServices(IServiceCollection services, KotodamaSettings settings)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new KnowledgeStore(settings.DatabasePath, provider.GetRequiredService<TimeProvider>(), settings.DreamTempStore));
        services.AddSingleton<McpToolUsageRecorder>();
        services.Configure<McpServerOptions>(options =>
        {
            options.ServerInstructions = KotodamaGuidance.ServerInstructions;
            options.ServerInfo = new()
            {
                Name = WindowsServiceName,
                Version = typeof(KotodamaApplication).Assembly.GetName().Version!.ToString(),
            };
        });
    }

    private static void AddMcpPrimitives(IMcpServerBuilder builder) =>
        builder.WithTools<KotodamaTools>()
            .WithPrompts<KotodamaPrompts>()
            .WithRequestFilters(filters => filters.AddCallToolFilter(McpToolUsageLogging.Wrap));

    private static void ConfigureLogging(ILoggingBuilder logging, string logDirectory)
    {
        // 権限を要する既定EventLog出力でToolエラー応答自体が失敗しないよう、出力先を明示します。
        logging.ClearProviders();
        logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        logging.AddProvider(new DailyFileLoggerProvider(logDirectory, TimeProvider.System));
    }

    private static async Task<int> BackupAsync(string destinationPath, KotodamaSettings settings)
    {
        var store = new KnowledgeStore(settings.DatabasePath, TimeProvider.System, settings.DreamTempStore);
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
