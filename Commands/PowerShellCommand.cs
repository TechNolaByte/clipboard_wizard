using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;

namespace ClipboardWizard.Commands;

/// <summary>
/// Opens a real terminal (Tabby if present, else a PowerShell window) with the clipboard text
/// pre-typed at the prompt but NOT executed — ready for the user to review and press Enter.
/// Routed through <see cref="Terminal"/> so it lands in Tabby via the single-token launcher.
/// </summary>
public sealed class PowerShellCommand : IClipboardCommand
{
    public string Name => "Execute as PowerShell";

    public CommandCategory Category => CommandCategory.Action;

    public bool CanExecute(ClipboardPayload payload) => payload.HasInput;

    public Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var content = payload.PrimaryText;
        if (content is null)
            return Task.CompletedTask;

        try
        {
            // The code to pre-type is handed off via a temp file so we don't have to escape
            // arbitrary PowerShell through a command line. The launched shell reads it, pre-fills
            // the prompt via PSReadLine, then deletes the temp file.
            var tempPath = Path.Combine(Path.GetTempPath(), $"clipwiz_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(tempPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var escapedPath = tempPath.Replace("'", "''");
            // Prefill the *interactive* prompt. PSReadLine isn't reading yet while the bootstrap
            // runs, so a direct Insert would no-op; defer it to the first OnIdle, which fires once
            // the interactive prompt is up and PSReadLine is active — the text then reliably lands
            // in the input buffer, awaiting Enter. Emitted as a *script file* (not an inline
            // -Command) and opened via Terminal, so it lands in Tabby (which needs -File, not
            // -Command — see Terminal) when Tabby is present, else a plain PowerShell window.
            // Tabby opens its shell in the home dir (it ignores ProcessStartInfo.WorkingDirectory),
            // so cd into working/ ourselves to keep terminal work alongside claude's, as documented.
            var bootstrapScript =
                "Set-Location -LiteralPath '" + AppPaths.WorkingRoot.Replace("'", "''") + "'\r\n" +
                "$global:__cwCode=[System.IO.File]::ReadAllText('" + escapedPath + "')\r\n" +
                "Remove-Item -LiteralPath '" + escapedPath + "' -ErrorAction SilentlyContinue\r\n" +
                "try { Import-Module PSReadLine -ErrorAction Stop } catch {}\r\n" +
                "$null=Register-EngineEvent -SourceIdentifier PowerShell.OnIdle -MaxTriggerCount 1 " +
                "-Action { try { [Microsoft.PowerShell.PSConsoleReadLine]::Insert($global:__cwCode) } catch {} }\r\n";

            // Space-free path (project working dir), as Terminal/Tabby require.
            var wrapper = Path.Combine(AppPaths.ScratchpadDir, $"pwsh_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(wrapper, bootstrapScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Terminal.RunScript(wrapper);

            ActionLog.Write("Execute as PowerShell",
                "Open a terminal in the working dir with the code pre-typed (awaiting Enter)",
                content, null,
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
