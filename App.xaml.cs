using System.IO;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;
using Forms = System.Windows.Forms;

namespace ClipboardWizard;

public partial class App : Application
{
    // A re-copy that lands within this window of the previous change is treated as one app writing
    // the clipboard twice for a single Ctrl+C (some apps do), not a human re-copying — so it stays
    // quiet. A deliberate re-copy is always slower than this.
    private const long RecopyMinGapMs = 250;

    private ClipboardMonitor? _monitor;
    private CommandRegistry? _registry;
    private CommandPopup? _popup;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _hawkFlush;
    private Forms.ToolStripMenuItem? _hawkCancel;
    private Forms.ToolStripMenuItem? _cycleStop;
    private bool _hawkWasActive;
    private string? _lastContentSignature;
    private long _lastChangeTick;

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

        Hawk.Changed = OnModeChanged;
        ClipboardCycle.Changed = OnModeChanged;
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        // The notification arrives on the UI thread already, but BeginInvoke yields so the
        // clipboard owner has finished writing before we read it.
        Dispatcher.BeginInvoke(new Action(HandleClipboardChange));
    }

    /// <summary>
    /// Decide whether a clipboard change should summon the popup. A single (fresh) copy is quiet;
    /// re-copying the same content summons the wizard. Identical bytes still bump the OS sequence
    /// number, so a re-copy reaches us as a second change event with a matching content signature.
    /// </summary>
    private void HandleClipboardChange()
    {
        var payload = ClipboardPayload.Capture();

        // Clipboard Hawk swallows the change: record it, no popup.
        if (Hawk.Active)
        {
            Hawk.Capture(payload);
            return;
        }

        // A genuine copy during a cycle ends it (our own fragment writes are suppressed, so
        // they never reach here).
        if (ClipboardCycle.Active)
            ClipboardCycle.Stop();

        var signature = payload.ContentSignature;
        var now = Environment.TickCount64;
        var isRecopy = signature == _lastContentSignature
            && signature != ClipboardPayload.EmptySignature
            && now - _lastChangeTick >= RecopyMinGapMs;
        _lastContentSignature = signature;
        _lastChangeTick = now;

        if (isRecopy)
            ShowPopup(payload);
    }

    /// <summary>Show the command popup for the given clipboard payload (no-op if nothing applies).</summary>
    private void ShowPopup(ClipboardPayload payload)
    {
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
        _cycleStop = new Forms.ToolStripMenuItem("Stop Clipboard Cycle", null, (_, _) => ClipboardCycle.Stop()) { Enabled = false };
        menu.Items.Add(_hawkFlush);
        menu.Items.Add(_hawkCancel);
        menu.Items.Add(_cycleStop);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
    }

    /// <summary>Refresh the tray + on-screen overlay for Hawk/Cycle state.</summary>
    private void OnModeChanged()
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
            _cycleStop.Enabled = ClipboardCycle.Active;
            _cycleStop.Text = ClipboardCycle.Active
                ? $"Stop Clipboard Cycle ({ClipboardCycle.Remaining}/{ClipboardCycle.Total})"
                : "Stop Clipboard Cycle";
        }

        var tip = Hawk.Active ? $"Clipboard Hawk: {Hawk.Count} item(s) — click to flush"
            : ClipboardCycle.Active ? $"Clipboard Cycle: {ClipboardCycle.Remaining} left — Ctrl+V for next"
            : "Clipboard Wizard";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;

        if (Hawk.Active && !_hawkWasActive)
            _tray.ShowBalloonTip(2500, "Clipboard Hawk", "Recording copies. Esc or click the tray to flush.", Forms.ToolTipIcon.Info);
        _hawkWasActive = Hawk.Active;

        // On-screen overlay pinned top-left while a mode is active.
        if (Hawk.Active)
            ModeOverlay.Update("🦅 Clipboard Hawk",
                $"{Hawk.Count} on stack · last: {(string.IsNullOrEmpty(Hawk.LastPreview) ? "—" : Hawk.LastPreview)}",
                Hawk.LastThumbnail);
        else if (ClipboardCycle.Active)
            ModeOverlay.Update("🔁 Clipboard Cycle",
                $"{ClipboardCycle.Remaining} left · next: {(string.IsNullOrEmpty(ClipboardCycle.NextPreview) ? "—" : ClipboardCycle.NextPreview)}",
                null);
        else
            ModeOverlay.Hide();
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
        Hawk.Cancel();
        ClipboardCycle.Stop();
        ModeOverlay.Hide();
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
