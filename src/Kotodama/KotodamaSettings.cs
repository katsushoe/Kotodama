using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kotodama;

/// <summary>Single application settings file for the Kotodama service.</summary>
internal sealed record KotodamaSettings
{
    internal const string ConfigurationPath = @"C:\Kotodama\config\kotodama.json";

    public string DatabasePath { get; init; } = @"C:\Kotodama\data\kotodama.db";
    public string LogDirectory { get; init; } = @"C:\Kotodama\logs";
    public DreamTempStore DreamTempStore { get; init; } = DreamTempStore.Default;
    public int DreamIntervalSeconds { get; init; } = 3600;
    public string Transport { get; init; } = "http";
    public string HttpUrl { get; init; } = ServerSettings.DefaultHttpUrl;
    public string? HttpToken { get; init; }

    internal string SourcePath { get; init; } = ConfigurationPath;

    internal ServerSettings GetServerSettings() => ServerSettings.Parse(Transport, HttpUrl, HttpToken);

    internal TimeSpan GetDreamInterval() => TimeSpan.FromSeconds(DreamIntervalSeconds > 0 ? DreamIntervalSeconds : 3600);

    internal static KotodamaSettings Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Kotodama settings file was not found: {fullPath}", fullPath);
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var settings = JsonSerializer.Deserialize<KotodamaSettings>(File.ReadAllText(fullPath), options)
            ?? throw new InvalidDataException($"Kotodama settings file is empty: {fullPath}");
        if (string.IsNullOrWhiteSpace(settings.DatabasePath)) throw new InvalidDataException("DatabasePath must not be empty.");
        if (string.IsNullOrWhiteSpace(settings.LogDirectory)) throw new InvalidDataException("LogDirectory must not be empty.");
        if (settings.DreamIntervalSeconds <= 0) throw new InvalidDataException("DreamIntervalSeconds must be greater than zero.");
        _ = settings.GetServerSettings();

        var configDirectory = Path.GetDirectoryName(fullPath)!;
        return settings with
        {
            SourcePath = fullPath,
            DatabasePath = Path.GetFullPath(settings.DatabasePath, configDirectory),
            LogDirectory = Path.GetFullPath(settings.LogDirectory, configDirectory),
        };
    }
}
