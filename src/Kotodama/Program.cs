using Kotodama;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    var databasePath = Environment.GetEnvironmentVariable("KOTODAMA_DB") ?? ApplicationPaths.GetDefaultDatabasePath(AppContext.BaseDirectory);
    var tempStoreText = Environment.GetEnvironmentVariable("KOTODAMA_DREAM_TEMP_STORE");
    var tempStore = Enum.TryParse<DreamTempStore>(tempStoreText, true, out var parsedTempStore) ? parsedTempStore : DreamTempStore.Default;
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton(provider => new KnowledgeStore(databasePath, provider.GetRequiredService<TimeProvider>(), tempStore));
    builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<KotodamaTools>();
    using var host = builder.Build();
    await host.Services.GetRequiredService<KnowledgeStore>().InitializeAsync();
    await host.RunAsync();
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Kotodama could not start: {exception.Message}");
    return 1;
}
