using System.Diagnostics;
using System.IO;

namespace ClipboardWizard.Services;

/// <summary>
/// Opens an interactive terminal running a PowerShell script file, preferring Tabby and falling
/// back to a PowerShell window.
///
/// We always launch a *script file* (never an inline command with the payload in it): Tabby's CLI
/// re-tokenizes its arguments, which mangles anything with spaces, quotes, or newlines. So all the
/// complex content lives inside the script and only its path is handed over.
///
/// Crucially we hand Tabby the .ps1 as a BARE POSITIONAL argument (<c>Tabby.exe "&lt;script&gt;.ps1"</c>),
/// NOT via <c>Tabby.exe run …</c>. Tabby's <c>run</c> subcommand ALWAYS pops a "Run …?" security
/// confirmation dialog (see tabby-local/src/cli.ts → <c>handleRunCommand</c>) with no setting to
/// disable it. Its positional-path handler (<c>OpenPathCLIHandler</c>) runs a .ps1/.bat/.sh in a
/// tab with no such prompt — so the positional form is what we use.
///
/// The positional handler runs the script under Tabby's built-in <c>powershell</c> profile with
/// <c>pauseAfterExit</c> (i.e. non-interactive: it runs the script, then pauses). So the script
/// must OWN the tab — launch an app (claude) or spawn its own interactive shell — and must
/// <c>Set-Location</c> itself, because the tab starts in the profile's home dir and gets no
/// forwarded working directory.
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

    /// <summary>Open a terminal that runs the given .ps1. In Tabby it runs via the positional-path
    /// handler (no "Run …?" prompt); without Tabby it falls back to a PowerShell window.</summary>
    public static void RunScript(string scriptPath)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = AppPaths.WorkingRoot,
        };

        if (TabbyPath is { } tabby)
        {
            // Bare positional path — Tabby's OpenPathCLIHandler runs it with no confirmation.
            // (`Tabby.exe run …` would, unavoidably, show one.) ArgumentList quotes it safely.
            psi.FileName = tabby;
            psi.ArgumentList.Add(scriptPath);
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
