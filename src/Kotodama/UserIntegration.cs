using System.Diagnostics;

namespace Kotodama;

/// <summary>ログオンユーザー向けのMCPクライアント設定を管理します。常駐起動はMSIが登録するWindowsサービスが担います。</summary>
internal static class UserIntegration
{
    /// <summary>0.18.1以前がログオン時起動に使っていたScheduled Task名です。移行時に削除します。</summary>
    internal const string LegacyTaskName = "Kotodama MCP Server";
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

        await RemoveLegacyTaskAsync(cancellationToken);
        try
        {
            CodexConfig.Update(GetCodexConfigPath(), McpUrl);
            CodexHookConfig.Update(GetCodexHooksPath(), executablePath);
            CodexAgentConfig.Update(GetCodexAgentPath(), GetCodexAgentTemplatePath(baseDirectory));
            return 0;
        }
        catch
        {
            CodexConfig.Remove(GetCodexConfigPath());
            CodexHookConfig.Remove(GetCodexHooksPath());
            CodexAgentConfig.Remove(GetCodexAgentPath());
            throw;
        }
    }

    internal static async Task<int> UnconfigureCodexAsync(CancellationToken cancellationToken = default)
    {
        CodexConfig.Remove(GetCodexConfigPath());
        CodexHookConfig.Remove(GetCodexHooksPath());
        CodexAgentConfig.Remove(GetCodexAgentPath());
        await RemoveLegacyTaskAsync(cancellationToken);
        return 0;
    }

    /// <summary>旧版のログオン時起動タスクを停止・削除します。存在しない場合は何もしません。</summary>
    internal static async Task RemoveLegacyTaskAsync(CancellationToken cancellationToken = default)
    {
        await RunOptionalAsync("schtasks.exe", ["/End", "/TN", LegacyTaskName], cancellationToken);
        await RunOptionalAsync("schtasks.exe", ["/Delete", "/TN", LegacyTaskName, "/F"], cancellationToken);
    }

    internal static string GetCodexConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");

    internal static string GetCodexHooksPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "hooks.json");

    internal static string GetCodexAgentPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "agents", CodexAgentConfig.FileName);

    internal static string GetCodexAgentTemplatePath(string baseDirectory) =>
        Path.Combine(baseDirectory, "codex", CodexAgentConfig.FileName);

    /// <summary>外部コマンドを実行し、起動失敗や非0終了は無視します。</summary>
    private static async Task RunOptionalAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
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
        if (process is null) return;

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await outputTask;
        await errorTask;
    }
}
