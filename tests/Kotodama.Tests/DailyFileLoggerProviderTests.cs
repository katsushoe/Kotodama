using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kotodama.Tests;

public sealed class DailyFileLoggerProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kotodama-logs-{Guid.NewGuid():N}");

    [Fact]
    public void LogInformation_WritesDailyLogFile()
    {
        var now = new DateTimeOffset(2026, 8, 29, 3, 0, 0, TimeSpan.Zero);
        using var provider = new DailyFileLoggerProvider(_root, new FixedTimeProvider(now));

        provider.CreateLogger("test.category").LogInformation("dream completed");

        var path = Path.Combine(_root, "kotodama-20260829.log");
        File.ReadAllText(path).Should().Contain("test.category").And.Contain("dream completed");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
