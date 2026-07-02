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
    private Forms.ToolStripMenuItem? _hawkFlush;
    private Forms.ToolStripMenuItem? _hawkCancel;
    private Forms.ToolStripMenuItem? _cycleStop;
    private bool _hawkWasActive;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Show a dialog (and log) instead of dying silently on an unhandled exception.
        CrashHandler.Install();

        // Single-instance: a new launch overrides the previous one (asking first if it's busy).
        SingleInstance.QuitRequested = () => Dispatcher.BeginInvoke(new Action(Shutdown));
        if (!SingleInstance.TryStart())
        {
            Shutdown();
            return;
        }

        AppPaths.InitializeWorkingArea();

        var scriptsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipboardWizard", "scripts");
        _registry = new CommandRegistry(scriptsDir);

        _monitor = new ClipboardMonitor();
        _monitor.ClipboardChanged += OnClipboardChanged;
        _monitor.Start();

        // Background features (Hawk, Cycle) write the clipboard outside a command, so give them the
        // same self-write suppression the commands get.
        AppState.SuppressNextClipboardChange = () => _monitor.SuppressNext();

        SetupTray();

        Hawk.Changed = UpdateModeTray;
        CycleClipboard.Changed = UpdateModeTray;
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

        // Clipboard Hawk swallows the change: record it, no popup.
        if (Hawk.Active)
        {
            Hawk.Capture(payload);
            return;
        }

        // A genuine copy during a cycle ends it (our own fragment writes are suppressed, so they
        // never reach here).
        if (CycleClipboard.Active)
            CycleClipboard.Stop();

        var commands = _registry!.GetCommands(payload);
        if (commands.Count == 0)
            return;

        if (_popup is not null)
        {
            var old = _popup;
            _popup = null;
            try { old.Close(); }
            catch (InvalidOperationException) { /* already closing — nothing to do */ }
        }

        var context = new CommandContext
        {
            SuppressNextClipboardChange = () => _monitor!.SuppressNext(),
        };

        var popup = new CommandPopup(payload, commands, context);
        // Only clear the field if this exact popup is still the current one (avoids a late Closed
        // from a previous popup nulling out the new one).
        popup.Closed += (_, _) => { if (ReferenceEquals(_popup, popup)) _popup = null; };
        _popup = popup;
        _popup.Show();
        _popup.Activate();
    }

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "Clipboard Wizard",
        };
        // Left-click flushes an active Clipboard Hawk.
        _tray.MouseClick += (_, ev) =>
        {
            if (ev.Button == Forms.MouseButtons.Left && Hawk.Active)
                Hawk.Flush();
        };

        var menu = new Forms.ContextMenuStrip();

        var verboseItem = new Forms.ToolStripMenuItem("Verbose (run in a terminal)")
        {
            CheckOnClick = true,
            Checked = AppState.Verbose,
            ToolTipText = "Open script/LLM runs in a visible terminal instead of applying them silently.",
        };
        verboseItem.CheckedChanged += (_, _) => AppState.Verbose = verboseItem.Checked;
        menu.Items.Add(verboseItem);

        menu.Items.Add("Open scripts folder", null, (_, _) =>
        {
            System.Diagnostics.Process.Start("explorer.exe", _registry!.ScriptsDirectory);
        });

        menu.Items.Add(new Forms.ToolStripSeparator());
        _hawkFlush = new Forms.ToolStripMenuItem("Flush Clipboard Hawk", null, (_, _) => Hawk.Flush()) { Enabled = false };
        _hawkCancel = new Forms.ToolStripMenuItem("Cancel Clipboard Hawk", null, (_, _) => Hawk.Cancel()) { Enabled = false };
        _cycleStop = new Forms.ToolStripMenuItem("Stop Cycle Clipboard", null, (_, _) => CycleClipboard.Stop()) { Enabled = false };
        menu.Items.Add(_hawkFlush);
        menu.Items.Add(_hawkCancel);
        menu.Items.Add(_cycleStop);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
    }

    /// <summary>Refresh the tray for Hawk/Cycle state (enabled items, counts, tooltip, balloon).</summary>
    private void UpdateModeTray()
    {
        if (_tray is null)
            return;

        if (_hawkFlush is not null)
        {
            _hawkFlush.Enabled = Hawk.Active;
            _hawkFlush.Text = Hawk.Active ? $"Flush Clipboard Hawk ({Hawk.Count})" : "Flush Clipboard Hawk";
        }
        if (_hawkCancel is not null)
            _hawkCancel.Enabled = Hawk.Active;
        if (_cycleStop is not null)
        {
            _cycleStop.Enabled = CycleClipboard.Active;
            _cycleStop.Text = CycleClipboard.Active
                ? $"Stop Cycle ({CycleClipboard.Position}/{CycleClipboard.Total})"
                : "Stop Cycle Clipboard";
        }

        var tip = Hawk.Active ? $"Clipboard Hawk: {Hawk.Count} item(s) — click to flush"
            : CycleClipboard.Active ? $"Cycle: {CycleClipboard.Position}/{CycleClipboard.Total} — Ctrl+V for next"
            : "Clipboard Wizard";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;

        if (Hawk.Active && !_hawkWasActive)
            _tray.ShowBalloonTip(2500, "Clipboard Hawk", "Recording copies. Click the tray icon to flush.", Forms.ToolTipIcon.Info);
        _hawkWasActive = Hawk.Active;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "clipwiz.ico");
            if (File.Exists(path))
                return new System.Drawing.Icon(path);
        }
        catch
        {
            // fall back below
        }
        return System.Drawing.SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        SingleInstance.Dispose();
        base.OnExit(e);
    }
}
