using System.Diagnostics;

namespace Kotodama;

/// <summary>ログオンユーザー向けの常駐起動とMCPクライアント設定を管理します。</summary>
internal static class UserIntegration
{
    internal const string TaskName = "Kotodama MCP Server";
    internal const string McpUrl = ServerSettings.DefaultHttpUrl + ServerSettings.HttpPath;

    internal static async Task<int> ConfigureAllAsync(string baseDirectory, CancellationToken cancellationToken = default)
    {
        await ConfigureCodexAsync(baseDirectory, cancellationToken);
        await ClaudeIntegration.ConfigureIfAvailableAsync(baseDirectory, cancellationToken);
        return 0;
    }

    internal static async Task<int> UnconfigureAllAsync(CancellationToken cancellationToken = default)
    {
        await ClaudeIntegration.UnconfigureIfAvailableAsync(cancellationToken);
        return await UnconfigureCodexAsync(cancellationToken);
    }

    internal static async Task<int> ConfigureCodexAsync(string baseDirectory, CancellationToken cancellationToken = default)
    {
        var executablePath = Path.Combine(baseDirectory, "Kotodama.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Kotodama.exe was not found.", executablePath);
        }

        try
        {
            await RunRequiredAsync("schtasks.exe", BuildCreateTaskArguments(executablePath), cancellationToken);
            CodexConfig.Update(GetCodexConfigPath(), McpUrl);
            CodexHookConfig.Update(GetCodexHooksPath(), executablePath);
            await RunRequiredAsync("schtasks.exe", ["/Run", "/TN", TaskName], cancellationToken);
            return 0;
        }
        catch
        {
            CodexConfig.Remove(GetCodexConfigPath());
            CodexHookConfig.Remove(GetCodexHooksPath());
            await RunOptionalAsync("schtasks.exe", ["/Delete", "/TN", TaskName, "/F"], cancellationToken);
            throw;
        }
    }

    internal static async Task<int> UnconfigureCodexAsync(CancellationToken cancellationToken = default)
    {
        CodexConfig.Remove(GetCodexConfigPath());
        CodexHookConfig.Remove(GetCodexHooksPath());
        await RunOptionalAsync("schtasks.exe", ["/End", "/TN", TaskName], cancellationToken);
        await RunOptionalAsync("schtasks.exe", ["/Delete", "/TN", TaskName, "/F"], cancellationToken);
        return 0;
    }

    internal static string[] BuildCreateTaskArguments(string executablePath) =>
    [
        "/Create",
        "/TN", TaskName,
        "/SC", "ONLOGON",
        "/TR", $"\"{executablePath}\" --http",
        "/RL", "LIMITED",
        "/F",
    ];

    internal static string GetCodexConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");

    internal static string GetCodexHooksPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "hooks.json");

    private static Task RunRequiredAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        RunAsync(fileName, arguments, true, cancellationToken);

    private static Task RunOptionalAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        RunAsync(fileName, arguments, false, cancellationToken);

    private static async Task RunAsync(string fileName, IReadOnlyList<string> arguments, bool required, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            if (required) throw new InvalidOperationException($"Could not start {fileName}.");
            return;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (required && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} failed with exit code {process.ExitCode}: {error}{output}".Trim());
        }
    }
}
