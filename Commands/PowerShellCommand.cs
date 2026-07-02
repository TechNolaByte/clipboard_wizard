using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;

namespace ClipboardWizard.Commands;

/// <summary>
/// Opens a real terminal (the user's default terminal app, via powershell.exe) with the clipboard
/// text pre-typed at the prompt but NOT executed — ready for the user to review and press Enter.
/// </summary>
public sealed class PowerShellCommand : IClipboardCommand
{
    public string Name => "Execute as PowerShell";

    public CommandCategory Category => CommandCategory.Action;

    public bool CanExecute(ClipboardPayload payload) => payload.HasText;

    public Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        if (!payload.HasText)
            return Task.CompletedTask;

        try
        {
            // The code is handed off via a temp file so we don't have to escape arbitrary
            // PowerShell through the command line. The launched shell reads it, pre-fills the
            // prompt via PSReadLine, then deletes the temp file.
            var tempPath = Path.Combine(Path.GetTempPath(), $"clipwiz_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(tempPath, payload.Text!, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var escapedPath = tempPath.Replace("'", "''");
            var bootstrap =
                "try { Import-Module PSReadLine -ErrorAction Stop; " +
                $"[Microsoft.PowerShell.PSConsoleReadLine]::Insert([System.IO.File]::ReadAllText('{escapedPath}')) }} catch {{ }} " +
                $"finally {{ Remove-Item -LiteralPath '{escapedPath}' -ErrorAction SilentlyContinue }}";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                // UseShellExecute => spawns its own terminal window and honours the OS default
                // terminal application (Windows Terminal on Windows 11, if configured).
                UseShellExecute = true,
                // Open in the project's working directory so terminal work stays alongside claude's.
                WorkingDirectory = AppPaths.WorkingRoot,
            };
            psi.ArgumentList.Add("-NoExit");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(bootstrap);

            Process.Start(psi);

            ActionLog.Write("Execute as PowerShell",
                "Open a terminal in the working dir with the code pre-typed (awaiting Enter)",
                payload.Text, null,
                $"Opened terminal in {AppPaths.WorkingRoot}",
                "(handed to terminal; not transformed here)", null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open a terminal:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }
}
