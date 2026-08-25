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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
