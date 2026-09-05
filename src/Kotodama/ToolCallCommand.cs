using System.Text.Json;
using ModelContextProtocol.Client;

namespace Kotodama;

/// <summary>稼働中サービスへMCP経由で業務操作を送るCLIです。</summary>
internal static class ToolCallCommand
{
    internal static async Task<int> RunAsync(string tool, string argumentsPath, ServerSettings settings, TextWriter output, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        await using var stream = File.OpenRead(argumentsPath);
        var arguments = await JsonSerializer.DeserializeAsync<Dictionary<string, object?>>(stream, cancellationToken: timeout.Token)
            ?? throw new ArgumentException("Tool arguments must be a JSON object.");
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(settings.HttpUrl ?? throw new ArgumentException("HTTP service URL is required."), ServerSettings.HttpPath),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(10),
        };
        if (settings.HttpToken is not null) options.AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {settings.HttpToken}" };
        await using var transport = new HttpClientTransport(options);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: timeout.Token);
        await output.WriteLineAsync(JsonSerializer.Serialize(result));
        return result.IsError == true ? 1 : 0;
    }
}
