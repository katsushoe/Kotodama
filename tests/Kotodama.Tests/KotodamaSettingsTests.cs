using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class KotodamaSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kotodama-settings-{Guid.NewGuid():N}");

    [Fact]
    public void Load_WhenConfigured_ResolvesPathsAndUsesJsonValues()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "kotodama.json");
        File.WriteAllText(path, """
            {
              "databasePath": "data/kotodama.db",
              "logDirectory": "logs",
              "dreamTempStore": "Memory",
              "dreamIntervalSeconds": 15,
              "transport": "http",
              "httpUrl": "http://127.0.0.1:39281",
              "httpToken": "test-token"
            }
            """);

        var settings = KotodamaSettings.Load(path);

        settings.DatabasePath.Should().Be(Path.Combine(_root, "data", "kotodama.db"));
        settings.LogDirectory.Should().Be(Path.Combine(_root, "logs"));
        settings.DreamTempStore.Should().Be(DreamTempStore.Memory);
        settings.GetDreamInterval().Should().Be(TimeSpan.FromSeconds(15));
        settings.GetServerSettings().HttpUrl.Should().Be(new Uri("http://127.0.0.1:39281"));
        settings.GetServerSettings().HttpToken.Should().Be("test-token");
        settings.SourcePath.Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public void Load_WhenLegacyEnvironmentVariableIsSet_UsesOnlyJsonValue()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "kotodama.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { DatabasePath = Path.Combine(_root, "from-file.db") }));
        var previousValue = Environment.GetEnvironmentVariable("KOTODAMA_DB");
        try
        {
            Environment.SetEnvironmentVariable("KOTODAMA_DB", Path.Combine(_root, "from-environment.db"));

            var settings = KotodamaSettings.Load(path);

            settings.DatabasePath.Should().Be(Path.Combine(_root, "from-file.db"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("KOTODAMA_DB", previousValue);
        }
    }

    [Fact]
    public void Load_WhenFileIsMissing_ThrowsWithoutUsingDefaults()
    {
        var action = () => KotodamaSettings.Load(Path.Combine(_root, "missing.json"));

        action.Should().Throw<FileNotFoundException>().WithMessage("*Kotodama settings file was not found*");
    }

    [Theory]
    [InlineData("stdio")]
    [InlineData("tcp")]
    public void Load_WhenTransportIsNotHttp_Throws(string transport)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "kotodama.json");
        File.WriteAllText(path, $$"""{"transport":"{{transport}}"}""");

        var action = () => KotodamaSettings.Load(path);

        action.Should().Throw<InvalidOperationException>().WithMessage("*Transport*");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
