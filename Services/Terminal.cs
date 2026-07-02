using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ClipboardWizard.Services;

/// <summary>
/// Opens interactive terminals. Prefers Tabby (the user's terminal) via <c>Tabby.exe run …</c>,
/// falling back to a PowerShell window. Used for "Act with" (interactive claude) and verbose runs.
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

    /// <summary>Open a terminal that runs a PowerShell command and stays open.</summary>
    public static void RunPwsh(string command)
    {
        var psi = NewPsi();
        if (TabbyPath is { } tabby)
        {
            psi.FileName = tabby;
            foreach (var a in new[] { "run", "pwsh", "-NoExit", "-NoProfile", "-Command", command })
                psi.ArgumentList.Add(a);
        }
        else
        {
            psi.FileName = "powershell.exe";
            foreach (var a in new[] { "-NoExit", "-Command", command })
                psi.ArgumentList.Add(a);
        }
        Process.Start(psi);
    }

    /// <summary>Open a terminal that runs an executable with args directly (e.g. interactive claude).</summary>
    public static void RunCommand(string exe, IEnumerable<string> args)
    {
        var psi = NewPsi();
        if (TabbyPath is { } tabby)
        {
            psi.FileName = tabby;
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(exe);
            foreach (var a in args)
                psi.ArgumentList.Add(a);
        }
        else
        {
            psi.FileName = "powershell.exe";
            psi.ArgumentList.Add("-NoExit");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(BuildPwshInvocation(exe, args));
        }
        Process.Start(psi);
    }

    private static ProcessStartInfo NewPsi() => new()
    {
        UseShellExecute = true,
        WorkingDirectory = AppPaths.WorkingRoot,
    };

    private static string BuildPwshInvocation(string exe, IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        sb.Append("& '").Append(exe.Replace("'", "''")).Append('\'');
        foreach (var a in args)
            sb.Append(" '").Append(a.Replace("'", "''")).Append('\'');
        return sb.ToString();
    }
}
