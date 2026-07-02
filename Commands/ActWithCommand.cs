using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Act with…": open an interactive Claude Code session in a Tabby terminal, seeded with the
/// clipboard content plus the user's instruction. It runs with normal permissions (auto mode — it
/// asks before risky actions). The prompt is passed via a wrapper script (read from a file), so no
/// multi-line/quoted content goes through Tabby's argument parser.
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
            var promptFile = Path.Combine(AppPaths.ScratchpadDir, $"actwith_{Guid.NewGuid():N}.txt");
            File.WriteAllText(promptFile, prompt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // Wrapper reads the prompt from the file and passes it to claude as a single argument,
            // so nothing complex rides on the command line. No -p / no bypass → interactive, follows
            // permissions.
            var wrapper = Path.Combine(AppPaths.ScratchpadDir, $"actwith_{Guid.NewGuid():N}.ps1");
            var script =
                $"Set-Location -LiteralPath '{Esc(AppPaths.WorkingRoot)}'\n" +
                $"& '{Esc(ClaudeCli.Executable)}' --model sonnet ([System.IO.File]::ReadAllText('{Esc(promptFile)}'))\n";
            File.WriteAllText(wrapper, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Terminal.RunScript(wrapper);
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

    private static string Esc(string s) => s.Replace("'", "''");

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
