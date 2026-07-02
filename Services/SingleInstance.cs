using System.Threading;
using System.Windows;

namespace ClipboardWizard.Services;

/// <summary>
/// Single-instance guard. A new instance overrides a previous one — unless the previous one is busy
/// running a command, in which case the user is asked to Wait, Override, or Cancel.
///
/// Mechanism: a named mutex marks the live instance; a named manual-reset event marks "busy"
/// (set around command execution); a named auto-reset event is how a new instance asks the old one
/// to quit. The primary listens on the quit event and shuts down when signalled.
/// </summary>
public static class SingleInstance
{
    private const string InstanceName = "ClipboardWizard.Instance.v1";
    private const string BusyName = "ClipboardWizard.Busy.v1";
    private const string QuitName = "ClipboardWizard.Quit.v1";

    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _busy;
    private static EventWaitHandle? _quit;

    /// <summary>Invoked on a background thread when another instance asks this one to exit.</summary>
    public static Action? QuitRequested;

    /// <summary>Negotiate single-instance. Returns true if this process should run.</summary>
    public static bool TryStart()
    {
        _instanceMutex = new Mutex(true, InstanceName, out var createdNew);
        if (createdNew)
        {
            BeginPrimary();
            return true;
        }

        // Another instance exists.
        if (ExistingBusy())
        {
            var choice = MessageBox.Show(
                "Another Clipboard Wizard is already running and is BUSY with a command.\n\n" +
                "Yes  —  wait for it to finish, then take over\n" +
                "No  —  override it now (may interrupt its work)\n" +
                "Cancel  —  don't launch this instance",
                "Clipboard Wizard", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (choice == MessageBoxResult.Cancel)
                return false;
            if (choice == MessageBoxResult.Yes)
                WaitUntilNotBusy();
            // No, or after waiting → fall through and override.
        }

        if (!TakeOver())
        {
            MessageBox.Show("Couldn't take over from the running Clipboard Wizard instance.",
                "Clipboard Wizard", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        BeginPrimary();
        return true;
    }

    /// <summary>Mark this instance busy (call around command execution).</summary>
    public static void EnterBusy() => _busy?.Set();

    /// <summary>Clear the busy flag.</summary>
    public static void ExitBusy() => _busy?.Reset();

    public static void Dispose()
    {
        try { _instanceMutex?.ReleaseMutex(); } catch { /* not owned */ }
        _instanceMutex?.Dispose();
        _busy?.Dispose();
        _quit?.Dispose();
    }

    private static void BeginPrimary()
    {
        _busy = new EventWaitHandle(false, EventResetMode.ManualReset, BusyName);
        _quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);

        var listener = new Thread(() =>
        {
            try { _quit!.WaitOne(); }
            catch { return; }
            QuitRequested?.Invoke();
        })
        {
            IsBackground = true,
            Name = "ClipWiz.QuitListener",
        };
        listener.Start();
    }

    private static bool ExistingBusy()
    {
        try
        {
            var e = EventWaitHandle.OpenExisting(BusyName);
            return e.WaitOne(0);
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch { return false; }
    }

    private static void WaitUntilNotBusy()
    {
        for (var i = 0; i < 4800; i++) // ~20 min ceiling
        {
            if (!ExistingBusy())
                return;
            Thread.Sleep(250);
        }
    }

    private static bool TakeOver()
    {
        try
        {
            EventWaitHandle.OpenExisting(QuitName).Set();
        }
        catch { /* old instance may already be gone */ }

        _instanceMutex?.Dispose();
        _instanceMutex = null;

        for (var i = 0; i < 100; i++) // up to ~20s for the old instance to exit
        {
            var m = new Mutex(true, InstanceName, out var createdNew);
            if (createdNew)
            {
                _instanceMutex = m;
                return true;
            }
            m.Dispose();
            Thread.Sleep(200);
        }
        return false;
    }
}
