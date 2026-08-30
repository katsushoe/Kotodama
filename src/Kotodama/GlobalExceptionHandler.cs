using Microsoft.Extensions.Logging;

namespace Kotodama;

/// <summary>プロセス境界で未処理例外を記録し、終了コードへ変換します。</summary>
internal sealed class GlobalExceptionHandler(ILogger logger) : IDisposable
{
    private bool _registered;

    /// <summary>プロセス全体の未処理例外イベントを監視します。</summary>
    internal void Register()
    {
        if (_registered)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _registered = true;
    }

    /// <summary>アプリケーションを実行し、予期しない例外を非0終了コードへ変換します。</summary>
    internal async Task<int> RunAsync(Func<Task<int>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return await action();
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "[GlobalException] Application terminated unexpectedly.");
            return 1;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _registered = false;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        if (eventArgs.ExceptionObject is Exception exception)
        {
            logger.LogCritical(exception, "[GlobalException] Unhandled process exception. Terminating: {IsTerminating}", eventArgs.IsTerminating);
            return;
        }

        logger.LogCritical("[GlobalException] Unhandled non-Exception object. Terminating: {IsTerminating}", eventArgs.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        logger.LogError(eventArgs.Exception, "[GlobalException] Unobserved task exception.");
        eventArgs.SetObserved();
    }
}
