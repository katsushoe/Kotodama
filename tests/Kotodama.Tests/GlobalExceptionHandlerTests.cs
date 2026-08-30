using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kotodama.Tests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task RunAsync_WhenActionSucceeds_ReturnsActionExitCode()
    {
        var logger = new RecordingLogger();
        using var handler = new GlobalExceptionHandler(logger);

        var result = await handler.RunAsync(() => Task.FromResult(7));

        result.Should().Be(7);
        logger.Exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenActionThrows_LogsExceptionAndReturnsOne()
    {
        var logger = new RecordingLogger();
        using var handler = new GlobalExceptionHandler(logger);
        var expected = new InvalidOperationException("health check failure");

        var result = await handler.RunAsync(() => Task.FromException<int>(expected));

        result.Should().Be(1);
        logger.Exceptions.Should().ContainSingle().Which.Should().BeSameAs(expected);
        logger.Levels.Should().ContainSingle().Which.Should().Be(LogLevel.Critical);
    }

    private sealed class RecordingLogger : ILogger
    {
        internal List<Exception> Exceptions { get; } = [];
        internal List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
            if (exception is not null)
            {
                Exceptions.Add(exception);
            }
        }
    }
}
