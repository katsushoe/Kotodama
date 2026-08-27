using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kotodama;

/// <summary>Claude Codeのユーザー設定へKotodama Hooksを安全に統合します。</summary>
internal static class ClaudeHookConfig
{
    private const string Marker = "--integration-id kotodama";

    internal static void Update(string path, string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var root = ReadRoot(path);
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        AddHook(hooks, "UserPromptSubmit", BuildCommand(executablePath, "user-prompt-submit"));
        AddHook(hooks, "Stop", BuildCommand(executablePath, "stop"));
        Write(path, root);
    }

    internal static void Remove(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return;

        var root = ReadRoot(path);
        if (root["hooks"] is not JsonObject hooks) return;

        RemoveHook(hooks, "UserPromptSubmit");
        RemoveHook(hooks, "Stop");
        if (hooks.Count == 0) root.Remove("hooks");
        Write(path, root);
    }

    internal static string BuildCommand(string executablePath, string eventName) =>
        $"\"{executablePath}\" hook claude {eventName} {Marker}";

    private static JsonObject ReadRoot(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("Claude settings root must be a JSON object.");
    }

    private static void AddHook(JsonObject hooks, string eventName, string command)
    {
        var matchers = hooks[eventName] as JsonArray ?? new JsonArray();
        hooks[eventName] = matchers;
        if (ContainsCommand(matchers, command)) return;

        matchers.Add(new JsonObject
        {
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                    ["timeout"] = 10,
                },
            },
        });
    }

    private static bool ContainsCommand(JsonArray matchers, string command) =>
        matchers.OfType<JsonObject>()
            .SelectMany(matcher => (matcher["hooks"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Any(hook => string.Equals(hook["command"]?.GetValue<string>(), command, StringComparison.Ordinal));

    private static void RemoveHook(JsonObject hooks, string eventName)
    {
        if (hooks[eventName] is not JsonArray matchers) return;

        foreach (var matcher in matchers.OfType<JsonObject>().ToArray())
        {
            if (matcher["hooks"] is not JsonArray commands) continue;
            foreach (var command in commands.OfType<JsonObject>()
                         .Where(IsKotodamaHook)
                         .ToArray())
            {
                commands.Remove(command);
            }

            if (commands.Count == 0) matchers.Remove(matcher);
        }

        if (matchers.Count == 0) hooks.Remove(eventName);
    }

    private static bool IsKotodamaHook(JsonObject hook) =>
        hook["command"]?.GetValue<string>().Contains(Marker, StringComparison.Ordinal) == true;

    private static void Write(string path, JsonObject root)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        File.Move(tempPath, path, true);
    }
}
