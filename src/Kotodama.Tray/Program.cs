using Microsoft.Extensions.Logging;

namespace Kotodama.Tray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(["--stop"], StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(@"Local\Kotodama.Tray.Stop");
                signal.Set();
                using var current = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var process in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
                {
                    using (process)
                    {
                        if (process.Id != current.Id && process.SessionId == current.SessionId &&
                            !process.WaitForExit(35000)) return 1;
                    }
                }
            }
            catch (WaitHandleCannotBeOpenedException) { return 0; }
            return 0;
        }
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\Kotodama.Tray", out var created);
        if (!created) return 0;

        using var bootstrap = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = bootstrap.CreateLogger("Kotodama.Tray");
        try
        {
            var settings = KotodamaSettings.Load(KotodamaSettings.ConfigurationPath);
            using var fileLogger = new DailyFileLoggerProvider(settings.LogDirectory, TimeProvider.System);
            using var logs = LoggerFactory.Create(builder => builder.AddProvider(fileLogger));
            logger = logs.CreateLogger("Kotodama.Tray");
            using var exceptions = new GlobalExceptionHandler(logger);
            exceptions.Register();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            ThreadExceptionEventHandler onError = (_, e) =>
            {
                logger.LogCritical(e.Exception, "[Tray] Unhandled UI exception.");
                MessageBox.Show(TrayText.Get("UnexpectedError"), "Kotodama", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            };
            Application.ThreadException += onError;
            try
            {
                using var context = new TrayContext(settings, logger);
                Application.Run(context);
                return context.ExitCode;
            }
            finally
            {
                Application.ThreadException -= onError;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            logger.LogError(exception, "[Tray] Configuration or storage is unavailable.");
            return 1;
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "[Tray] Startup failed.");
            MessageBox.Show(TrayText.Get("UnexpectedError"), "Kotodama", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
