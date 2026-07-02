using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ClipboardWizard.UI;

namespace ClipboardWizard.Services;

/// <summary>
/// Global last-resort error reporting: instead of the app dying silently, show a dialog with the
/// details (and write a crash log to <c>working/logs/</c>). UI-thread exceptions are marked handled
/// so the tray app keeps running; genuinely fatal background failures are still surfaced first.
/// </summary>
public static class CrashHandler
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;

        if (System.Windows.Application.Current is { } app)
            app.DispatcherUnhandledException += OnDispatcher;
        AppDomain.CurrentDomain.UnhandledException += OnDomain;
        TaskScheduler.UnobservedTaskException += OnTask;
    }

    private static void OnDispatcher(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception, "UI thread");
        e.Handled = true; // recover — a single failing command shouldn't take down the tray app
    }

    private static void OnDomain(object? sender, UnhandledExceptionEventArgs e)
    {
        Report(e.ExceptionObject as Exception, e.IsTerminating ? "background thread (fatal)" : "background thread");
    }

    private static void OnTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Report(e.Exception, "background task");
        e.SetObserved();
    }

    private static void Report(Exception? ex, string where)
    {
        var details = ex?.ToString() ?? "Unknown error.";

        string? logPath = null;
        try
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
            logPath = Path.Combine(AppPaths.LogsDir, $"crash_{stamp}.log");
            File.WriteAllText(logPath, $"Clipboard Wizard crash ({where}) at {DateTime.Now}\n\n{details}");
        }
        catch
        {
            // best effort
        }

        try
        {
            var body = $"Something went wrong ({where}).\n\n" +
                       (logPath is null ? "" : $"Logged to: {logPath}\n\n") +
                       details;

            void ShowIt() => Prompts.ShowResult("Clipboard Wizard — error", body);

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
                dispatcher.Invoke(ShowIt);
            else
                ShowIt();
        }
        catch
        {
            // never let the crash handler itself throw
        }
    }
}
