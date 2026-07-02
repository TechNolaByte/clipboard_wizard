using System.Diagnostics;
using System.IO;

namespace ClipboardWizard.Services;

/// <summary>
/// Opens an interactive terminal running a PowerShell script file, preferring Tabby
/// (<c>Tabby.exe run …</c>) and falling back to a PowerShell window.
///
/// We always launch a *script file* (never an inline command with the payload in it): Tabby's
/// <c>run</c> re-tokenizes its arguments, which mangles anything with spaces, quotes, or newlines.
/// So all the complex content lives inside the script and only its (space-free) path is passed.
///
/// The catch with Tabby specifically: its CLI parses with yargs, which SWALLOWS any leading-dash
/// token (<c>-NoExit</c>, <c>-File</c>, …) as one of its own options — so
/// <c>Tabby.exe run pwsh -NoExit -File script.ps1</c> loses every flag and just opens a blank
/// default tab. The fix is to hand <c>run</c> a single, dashless launcher <c>.cmd</c> (see
/// <see cref="EnsureTabbyLauncher"/>) with the script path as its one positional argument; the
/// launcher applies the pwsh flags itself, out of yargs' reach.
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
            // `Tabby.exe run <launcher.cmd> <scriptPath>` — a single dashless launcher token so
            // yargs can't eat the pwsh flags (which it would if we passed them directly). The
            // launcher applies -NoExit/-File itself; scriptPath rides as its lone positional arg.
            var launcher = EnsureTabbyLauncher();
            psi.FileName = tabby;
            foreach (var a in new[] { "run", launcher, scriptPath })
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

    /// <summary>
    /// Write (idempotently) the tiny launcher <c>.cmd</c> that <c>Tabby.exe run</c> invokes, and
    /// return its path. It takes the .ps1 path as <c>%1</c> and runs it under pwsh with the flags
    /// yargs would otherwise swallow. Must be CRLF so <c>cmd</c> parses it correctly.
    /// </summary>
    private static string EnsureTabbyLauncher()
    {
        var path = Path.Combine(AppPaths.ScratchpadDir, "tabby-run-file.cmd");
        const string body =
            "@echo off\r\n" +
            "rem Single-token launcher for `Tabby.exe run`. Tabby's yargs CLI swallows leading-dash\r\n" +
            "rem args (-NoExit/-File/...) as its own options, so the pwsh flags must live here, not on\r\n" +
            "rem Tabby's command line. Tabby passes the .ps1 path as %1.\r\n" +
            "pwsh -NoExit -NoProfile -ExecutionPolicy Bypass -File \"%~1\"\r\n";
        if (!File.Exists(path) || File.ReadAllText(path) != body)
            File.WriteAllText(path, body, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
