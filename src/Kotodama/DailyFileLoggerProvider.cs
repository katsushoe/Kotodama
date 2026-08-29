using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Kotodama;

/// <summary>日別ログファイルへ追記するLogger Providerです。</summary>
internal sealed class DailyFileLoggerProvider(string logDirectory, TimeProvider timeProvider) : ILoggerProvider
{
    private readonly object _sync = new();

    public ILogger CreateLogger(string categoryName) => new DailyFileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            var now = timeProvider.GetLocalNow();
            var path = Path.Combine(logDirectory, $"kotodama-{now:yyyyMMdd}.log");
            var line = $"{now:O} [{level}] {category}: {message}{(exception is null ? string.Empty : Environment.NewLine + exception)}{Environment.NewLine}";
            lock (_sync) File.AppendAllText(path, line, System.Text.Encoding.UTF8);
        }
        catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Kotodama file logging failed: {writeException.Message}");
        }
    }

    private sealed class DailyFileLogger(DailyFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel)) provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
