using System.ComponentModel;
using System.Diagnostics;

namespace Kotodama;

/// <summary>Claude CodeのユーザースコープMCP設定を管理します。</summary>
internal static class ClaudeIntegration
{
    internal static string[] BuildAddArguments() =>
        ["mcp", "add", "--transport", "http", "--scope", "user", "kotodama", UserIntegration.McpUrl];

    internal static string[] BuildRemoveArguments() =>
        ["mcp", "remove", "--scope", "user", "kotodama"];

    internal static async Task<int> ConfigureAsync(CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable() ?? throw new InvalidOperationException("Claude Code is not installed or claude.exe is not on PATH.");
        await RunAsync(executable, BuildRemoveArguments(), false, cancellationToken);
        await RunAsync(executable, BuildAddArguments(), true, cancellationToken);
        return 0;
    }

    internal static async Task<int> UnconfigureAsync(CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable() ?? throw new InvalidOperationException("Claude Code is not installed or claude.exe is not on PATH.");
        await RunAsync(executable, BuildRemoveArguments(), false, cancellationToken);
        return 0;
    }

    internal static Task<int> ConfigureIfAvailableAsync(CancellationToken cancellationToken = default) =>
        FindExecutable() is null ? Task.FromResult(0) : ConfigureAsync(cancellationToken);

    internal static Task<int> UnconfigureIfAvailableAsync(CancellationToken cancellationToken = default) =>
        FindExecutable() is null ? Task.FromResult(0) : UnconfigureAsync(cancellationToken);

    internal static string? FindExecutable()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim().Trim('"'), "claude.exe");
            if (File.Exists(candidate)) return candidate;
        }

        var packagesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(packagesDirectory))
        {
            var candidate = Directory.EnumerateFiles(packagesDirectory, "claude.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains("Anthropic.ClaudeCode_", StringComparison.OrdinalIgnoreCase));
            if (candidate is not null) return candidate;
        }

        return null;
    }

    private static async Task RunAsync(string executable, IReadOnlyList<string> arguments, bool required, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Claude Code could not start.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            if (required && process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Claude Code failed with exit code {process.ExitCode}: {error}{output}".Trim());
            }
        }
        catch (Win32Exception) when (!required)
        {
        }
    }
}
