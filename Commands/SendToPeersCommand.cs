using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Send to peers": run the clipboard content on the fleet via <c>fleet.ps1 run</c>. Optionally
/// scoped to named peers (<c>-Only</c>); blank targets all machines. Uses the fleet's <c>-File</c>
/// mode so the content is read verbatim (no shell-expansion footgun) and <c>-Json</c> output.
/// </summary>
public sealed class SendToPeersCommand : IClipboardCommand
{
    private static readonly string FleetScript =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "fleet", "fleet.ps1");

    public string Name => "Send to peers (fleet)";

    public CommandCategory Category => CommandCategory.Action;

    public bool CanExecute(ClipboardPayload payload) => payload.HasInput;

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var content = payload.PrimaryText;
        if (content is null)
            return;

        if (!File.Exists(FleetScript))
        {
            MessageBox.Show($"fleet.ps1 not found at:\n{FleetScript}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var peers = Prompts.AskText("Send to peers (fleet)",
            "Which peers? Comma-separated names (e.g. magma,bismuth), or leave blank for all.",
            context: content.Length > 1200 ? content[..1200] + "\n…" : content);
        if (peers is null)
            return; // cancelled

        var payloadFile = Path.Combine(AppPaths.ScratchpadDir, $"peers_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(payloadFile, content);

        var args = new List<string>
        {
            "-NoProfile", "-File", FleetScript, "run", "-File", payloadFile, "-Json",
        };
        if (!string.IsNullOrWhiteSpace(peers))
        {
            args.Add("-Only");
            args.Add(peers.Trim());
        }

        ProcResult result;
        StatusToast.Show("Sending to peers…");
        try
        {
            result = await Proc.RunAsync(PwshPath, args, workingDir: Path.GetDirectoryName(FleetScript));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't run fleet:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            StatusToast.Hide();
        }

        var output = string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
        ActionLog.Write("Send to peers", peers.Length == 0 ? "(all peers)" : peers, content, null,
            $"exit {result.ExitCode}\n\n{result.StdErr}", output, null);

        Prompts.ShowResult("Send to peers — result", string.IsNullOrWhiteSpace(output) ? "(no output)" : output);
    }

    private static string PwshPath
    {
        get
        {
            const string full = @"C:\Program Files\PowerShell\7\pwsh.exe";
            return File.Exists(full) ? full : "pwsh";
        }
    }
}
