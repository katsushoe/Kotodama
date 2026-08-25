using System.Text;

namespace Kotodama;

/// <summary>CodexのKotodama MCPセクションを他設定を保持したまま管理します。</summary>
internal static class CodexConfig
{
    private const string SectionHeader = "[mcp_servers.kotodama]";

    internal static void Update(string path, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var content = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var updated = RemoveSection(content).TrimEnd() + $"{Environment.NewLine}{Environment.NewLine}{SectionHeader}{Environment.NewLine}url = \"{url}\"{Environment.NewLine}";
        WriteAtomically(path, updated);
    }

    internal static void Remove(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return;
        WriteAtomically(path, RemoveSection(File.ReadAllText(path)));
    }

    internal static string RemoveSection(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var output = new List<string>(lines.Length);
        var skipping = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[mcp_servers.kotodama", StringComparison.OrdinalIgnoreCase))
            {
                skipping = true;
                continue;
            }

            if (skipping && trimmed.StartsWith('[')) skipping = false;
            if (!skipping) output.Add(line);
        }

        return string.Join(Environment.NewLine, output).TrimEnd() + Environment.NewLine;
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Codex config directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".kotodama.tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }
}
