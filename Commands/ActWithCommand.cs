using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Act with…": open an interactive Claude Code session in a Tabby terminal, pointed at the
/// clipboard content plus the user's instruction. It runs with normal permissions (auto mode —
/// it asks before risky actions) so the user stays in control and watches it work in the terminal.
/// </summary>
public sealed class ActWithCommand : IClipboardCommand
{
    public string Name => "Act with… (Claude Code)";

    public CommandCategory Category => CommandCategory.Action;

    public bool CanExecute(ClipboardPayload payload) => payload.HasInput || payload.HasImage;

    public Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        string content;
        try
        {
            content = StageClipboard(payload);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't prepare the clipboard content:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return Task.CompletedTask;
        }

        var instruction = Prompts.AskText("Act with… (Claude Code)",
            "What should Claude do with this clipboard content?\n\n" +
            "It opens in a terminal and runs with normal permissions — it will ask before risky actions.",
            multiline: true,
            context: content);
        if (string.IsNullOrWhiteSpace(instruction))
            return Task.CompletedTask;

        var prompt = $"{instruction}\n\n--- Clipboard content ---\n{content}";

        try
        {
            // Interactive claude in Tabby: no -p, no bypass — auto mode on, follows permissions.
            Terminal.RunCommand(ClaudeCli.Executable, new[] { "--model", "sonnet", prompt });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open a Claude terminal:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return Task.CompletedTask;
        }

        ActionLog.Write("Act with", instruction, content, null,
            "Opened an interactive Claude Code session in a terminal (normal permissions).",
            "(interactive — see the terminal)", null);

        return Task.CompletedTask;
    }

    /// <summary>Return a compact description of the clipboard, staging large text/images to files.</summary>
    private static string StageClipboard(ClipboardPayload payload)
    {
        if (payload.HasText)
        {
            if (payload.Text!.Length <= 4000)
                return payload.Text!;
            var staged = Path.Combine(AppPaths.ScratchpadDir, $"clipboard_{Guid.NewGuid():N}.txt");
            File.WriteAllText(staged, payload.Text!);
            return $"(large clipboard text saved to {staged} — read that file)";
        }

        if (payload.HasFiles)
            return "Files on the clipboard:\n" + string.Join('\n', payload.Files!.Select(f => "- " + f));

        if (payload.HasImage)
        {
            var staged = ImageIO.SavePng(payload.Image!, AppPaths.ScratchpadDir);
            return $"(clipboard image saved to {staged} — read that file to view it)";
        }

        return "(clipboard is empty)";
    }
}
