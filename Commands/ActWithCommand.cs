using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Act with…": run Claude Code agentically on the clipboard content with full tool access
/// (computer-control auto mode). Stakes are clarified up front (instruction + explicit
/// confirmation), and the captured output — which is asked to end with a "Changes made" summary —
/// is shown afterwards so the user knows what happened.
/// </summary>
public sealed class ActWithCommand : IClipboardCommand
{
    // Inline small payloads; stage large ones to a file so we don't blow the command-line limit.
    private const int InlineLimit = 8000;

    public string Name => "Act with… (Claude Code)";

    public CommandCategory Category => CommandCategory.Action;

    public bool CanExecute(ClipboardPayload payload) => payload.HasText || payload.HasFiles || payload.HasImage;

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var instruction = Prompts.AskText("Act with… (Claude Code)",
            "What should Claude do with this clipboard content?\n\n" +
            "It runs with FULL tool access — it can edit files and run commands.",
            multiline: true);
        if (string.IsNullOrWhiteSpace(instruction))
            return;

        var workingDir = AppPaths.WorkingRoot;

        if (!Prompts.Confirm("Act with — confirm stakes",
                $"Claude Code will run with FULL permissions (it can edit files and run commands) in:\n" +
                $"{workingDir}\n\n" +
                $"Instruction:\n{instruction}\n\nProceed?"))
            return;

        string content;
        try
        {
            content = DescribeClipboard(payload);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't prepare the clipboard content:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var prompt =
            $"{instruction}\n\n--- Clipboard content ---\n{content}\n\n" +
            "When finished, end your reply with a section titled '## Changes made' that lists every " +
            "file you created, modified, or deleted and every command you ran that had side effects. " +
            "If you made no changes, say so explicitly.";

        ClaudeResult result;
        try
        {
            result = await ClaudeCli.RunAgenticAsync(prompt, workingDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't run claude:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var summary = result.Success
            ? (string.IsNullOrWhiteSpace(result.Output) ? "(claude produced no output)" : result.Output)
            : $"claude exited with code {result.ExitCode}:\n\n{result.FailureMessage}";

        ActionLog.Write("Act with", instruction, content, null,
            $"stdout:\n{result.Output}\n\nstderr:\n{result.Error}", summary, null);

        Prompts.ShowResult("Act with — result", summary);
    }

    private static string DescribeClipboard(ClipboardPayload payload)
    {
        if (payload.HasText)
        {
            if (payload.Text!.Length <= InlineLimit)
                return payload.Text!;

            var staged = Path.Combine(AppPaths.ScratchpadDir, $"clipboard_{Guid.NewGuid():N}.txt");
            File.WriteAllText(staged, payload.Text!);
            return $"(The clipboard text is large; it has been saved to: {staged}\n" +
                   "Read that file to get the full content.)";
        }

        if (payload.HasFiles)
            return "Files on the clipboard:\n" + string.Join('\n', payload.Files!.Select(f => "- " + f));

        if (payload.HasImage)
        {
            var staged = ImageIO.SavePng(payload.Image!, AppPaths.ScratchpadDir);
            return $"(An image is on the clipboard; it has been saved to: {staged}\n" +
                   "Read that file if you need to view it.)";
        }

        return "(clipboard is empty)";
    }
}
