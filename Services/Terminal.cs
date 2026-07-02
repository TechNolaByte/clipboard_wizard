using System.Diagnostics;
using System.IO;

namespace ClipboardWizard.Services;

/// <summary>
/// Opens an interactive terminal running a PowerShell script file, preferring Tabby
/// (<c>Tabby.exe run …</c>) and falling back to a PowerShell window.
///
/// We always launch a *script file* (never an inline command with the payload in it): Tabby's
/// <c>run</c> re-tokenizes its arguments, which mangles anything with spaces, quotes, or newlines.
/// A `-File &lt;path&gt;` invocation has no such payload on the command line (the path is
/// space-free), so it survives; all the complex content lives inside the script.
/// </summary>
public static class Terminal
{
    public static string? TabbyPath
    {
        get
        {
            var p = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "tabby", "Tabby.exe");
            return File.Exists(p) ? p : null;
        }
    }

    /// <summary>Open a terminal that runs the given .ps1 and stays open afterwards.</summary>
    public static void RunScript(string scriptPath)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = AppPaths.WorkingRoot,
        };

        if (TabbyPath is { } tabby)
        {
            psi.FileName = tabby;
            foreach (var a in new[] { "run", "pwsh", "-NoExit", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath })
                psi.ArgumentList.Add(a);
        }
        else
        {
            psi.FileName = "powershell.exe";
            foreach (var a in new[] { "-NoExit", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath })
                psi.ArgumentList.Add(a);
        }

        Process.Start(psi);
    }
}
