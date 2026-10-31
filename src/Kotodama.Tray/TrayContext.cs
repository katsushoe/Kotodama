using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Kotodama.Tray;

/// <summary>ユーザーセッション内でHTTPホストと通知領域アイコンを管理します。</summary>
internal sealed partial class TrayContext : ApplicationContext
{
    private KotodamaSettings _settings;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _ui;
    private CancellationTokenSource? _stop;
    private Task? _server;
    private bool _closing;
    private readonly EventWaitHandle _stopSignal = new(false, EventResetMode.AutoReset, @"Local\Kotodama.Tray.Stop");
    private bool _started;
    internal int ExitCode { get; private set; }

    internal TrayContext(KotodamaSettings settings, ILogger logger)
    {
        _settings = settings;
        _logger = logger;
        _ui = SynchronizationContext.Current!;
        InitializeComponent();
    }

    private void StartupTick(object? sender, EventArgs e)
    {
        if (_stopSignal.WaitOne(0))
        {
            _startup.Stop();
            ExitClicked(sender, e);
            return;
        }
        if (_started) return;
        _started = true;
        StartServer();
    }

    private void StartServer()
    {
        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        SetStatus("Starting");
        _server = Task.Run(async () =>
        {
            try
            {
                await KotodamaApplication.RunHttpAsync([], _settings, token,
                    () => _ui.Post(_ => { if (!_closing) SetStatus("Running"); }, null));
                _ui.Post(_ => { if (!_closing) SetStatus("Stopped"); }, null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _logger.LogInformation("[Tray] Host stopped.");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "[Tray] HTTP host failed.");
                _ui.Post(_ => { if (!_closing) { ExitCode = 1; SetStatus("Failed"); } }, null);
            }
        });
    }

    private async Task StopServerAsync()
    {
        if (_stop is not null) await _stop.CancelAsync();
        if (_server is not null) await _server;
        _stop?.Dispose();
        _stop = null;
        _server = null;
    }

    private async void RestartClicked(object? sender, EventArgs e)
    {
        _restart.Enabled = false;
        _exit.Enabled = false;
        try
        {
            await StopServerAsync();
            _settings = KotodamaSettings.Load(KotodamaSettings.ConfigurationPath);
            ExitCode = 0;
            StartServer();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[Tray] Restart failed.");
            ExitCode = 1;
            SetStatus("Failed");
        }
        finally
        {
            _restart.Enabled = true;
            _exit.Enabled = true;
        }
    }

    private void LogsClicked(object? sender, EventArgs e) => OpenPath(_settings.LogDirectory);
    private void ConfigClicked(object? sender, EventArgs e) => OpenPath(KotodamaSettings.ConfigurationPath);

    private void OpenPath(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[Tray] Could not open selected path.");
            MessageBox.Show(TrayText.Get("UnexpectedError"), "Kotodama", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void ExitClicked(object? sender, EventArgs e)
    {
        _closing = true;
        _menu.Enabled = false;
        await StopServerAsync();
        ExitThread();
    }

    private void SetStatus(string resource)
    {
        _status.Text = TrayText.Get(resource);
        _icon.Text = _status.Text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Visible = false;
            _components?.Dispose();
            _stop?.Dispose();
            _stopSignal.Dispose();
        }
        base.Dispose(disposing);
    }
}
