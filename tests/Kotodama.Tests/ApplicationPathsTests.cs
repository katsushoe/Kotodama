using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class ApplicationPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kotodama-paths-{Guid.NewGuid():N}");

    [Fact]
    public void GetDefaultDatabasePath_WhenInstalledDataExists_ReturnsDataPath()
    {
        var binDirectory = Path.Combine(_root, "bin");
        var dataDirectory = Path.Combine(_root, "data");
        Directory.CreateDirectory(binDirectory);
        Directory.CreateDirectory(dataDirectory);

        var result = ApplicationPaths.GetDefaultDatabasePath(binDirectory);

        result.Should().Be(Path.Combine(dataDirectory, "kotodama.db"));
    }

    [Fact]
    public void GetDefaultDatabasePath_WhenDataDoesNotExist_ReturnsExecutablePath()
    {
        var binDirectory = Path.Combine(_root, "bin");
        Directory.CreateDirectory(binDirectory);

        var result = ApplicationPaths.GetDefaultDatabasePath(binDirectory);

        result.Should().Be(Path.Combine(binDirectory, "kotodama.db"));
    }

    [Fact]
    public void GetLogDirectory_WhenInstalledLogsExists_ReturnsSiblingDirectory()
    {
        var binDirectory = Path.Combine(_root, "bin");
        var logsDirectory = Path.Combine(_root, "logs");
        Directory.CreateDirectory(binDirectory);
        Directory.CreateDirectory(logsDirectory);

        ApplicationPaths.GetLogDirectory(binDirectory).Should().Be(logsDirectory);
    }

    [Fact]
    public void GetLogDirectory_WhenEnvironmentIsConfigured_ReturnsConfiguredPath()
    {
        var configuredDirectory = Path.Combine(_root, "configured-logs");
        var previousValue = Environment.GetEnvironmentVariable("KOTODAMA_LOG_DIR");
        try
        {
            Environment.SetEnvironmentVariable("KOTODAMA_LOG_DIR", configuredDirectory);

            ApplicationPaths.GetLogDirectory(_root).Should().Be(Path.GetFullPath(configuredDirectory));
        }
        finally
        {
            Environment.SetEnvironmentVariable("KOTODAMA_LOG_DIR", previousValue);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
