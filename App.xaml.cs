using System.IO;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;
using Forms = System.Windows.Forms;

namespace ClipboardWizard;

public partial class App : Application
{
    private ClipboardMonitor? _monitor;
    private CommandRegistry? _registry;
    private CommandPopup? _popup;
    private Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var scriptsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipboardWizard", "scripts");
        _registry = new CommandRegistry(scriptsDir);

        _monitor = new ClipboardMonitor();
        _monitor.ClipboardChanged += OnClipboardChanged;
        _monitor.Start();

        SetupTray();
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        // The notification arrives on the UI thread already, but BeginInvoke yields so the
        // clipboard owner has finished writing before we read it.
        Dispatcher.BeginInvoke(ShowPopup);
    }

    private void ShowPopup()
    {
        var payload = ClipboardPayload.Capture();
        var commands = _registry!.GetCommands(payload);
        if (commands.Count == 0)
            return;

        if (_popup is not null)
        {
            _popup.Close();
            _popup = null;
        }

        var context = new CommandContext
        {
            SuppressNextClipboardChange = () => _monitor!.SuppressNext(),
        };

        _popup = new CommandPopup(payload, commands, context);
        _popup.Closed += (_, _) => _popup = null;
        _popup.Show();
        _popup.Activate();
    }

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Clipboard Wizard",
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open scripts folder", null, (_, _) =>
        {
            System.Diagnostics.Process.Start("explorer.exe", _registry!.ScriptsDirectory);
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }
}
