using System.Text;

namespace Kotodama;

/// <summary>CodexのユーザースコープへKotodama知識整理Agentを登録します。</summary>
internal static class CodexAgentConfig
{
    internal const string FileName = "kotodama-curator.toml";

    internal static void Update(string path, string templatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("Kotodama curator agent template was not found.", templatePath);
        }

        WriteAtomically(path, File.ReadAllText(templatePath));
    }

    internal static void Remove(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path)) File.Delete(path);
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Codex agents directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".kotodama.tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }
}
