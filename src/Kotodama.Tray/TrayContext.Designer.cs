#nullable enable
namespace Kotodama.Tray;

internal sealed partial class TrayContext
{
    private System.ComponentModel.IContainer? _components;
    private NotifyIcon _icon = null!;
    private ContextMenuStrip _menu = null!;
    private ToolStripMenuItem _status = null!;
    private ToolStripMenuItem _restart = null!;
    private ToolStripMenuItem _logs = null!;
    private ToolStripMenuItem _config = null!;
    private ToolStripMenuItem _exit = null!;
    private System.Windows.Forms.Timer _startup = null!;

    private void InitializeComponent()
    {
        _components = new System.ComponentModel.Container();
        _menu = new ContextMenuStrip(_components);
        _status = new ToolStripMenuItem();
        _restart = new ToolStripMenuItem();
        _logs = new ToolStripMenuItem();
        _config = new ToolStripMenuItem();
        _exit = new ToolStripMenuItem();
        _startup = new System.Windows.Forms.Timer(_components);
        _icon = new NotifyIcon(_components);
        _status.Enabled = false;
        _status.Text = TrayText.Get("Starting");
        _restart.Text = TrayText.Get("Restart");
        _restart.Click += RestartClicked;
        _logs.Text = TrayText.Get("Logs");
        _logs.Click += LogsClicked;
        _config.Text = TrayText.Get("Config");
        _config.Click += ConfigClicked;
        _exit.Text = TrayText.Get("Exit");
        _exit.Click += ExitClicked;
        _menu.Items.AddRange([_status, _restart, _logs, _config, _exit]);
        _icon.ContextMenuStrip = _menu;
        _icon.Icon = System.Drawing.SystemIcons.Application;
        _icon.Text = TrayText.Get("Starting");
        _icon.Visible = true;
        _startup.Interval = 100;
        _startup.Tick += StartupTick;
        _startup.Start();
    }
}
