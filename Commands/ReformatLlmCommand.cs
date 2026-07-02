using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Reformat in situ — LLM": ask for a reformat instruction, pipe the clipboard text through a
/// lean, tool-less <c>claude</c> (Sonnet) run, and replace the clipboard with the result.
/// </summary>
public sealed class ReformatLlmCommand : IClipboardCommand
{
    private const string SystemPrompt =
        "You transform text. The text to transform is provided on standard input. Apply the user's " +
        "instruction and output ONLY the resulting text — no explanations, no preamble, and no " +
        "surrounding markdown code fences.";

    public string Name => "Reformat in situ — LLM";

    public CommandCategory Category => CommandCategory.Action;

    public bool CanExecute(ClipboardPayload payload) => payload.HasText;

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        if (!payload.HasText)
            return;

        var spec = Prompts.AskText("Reformat — LLM", "How should I reformat the clipboard text?");
        if (string.IsNullOrWhiteSpace(spec))
            return;

        ClaudeResult result;
        try
        {
            result = await ClaudeCli.RunTextAsync(spec, payload.Text, SystemPrompt);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't run claude:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var processLog = $"claude stdout:\n{result.Output}\n\nstderr:\n{result.Error}";
        if (!result.Success || string.IsNullOrEmpty(result.Output))
        {
            ActionLog.Write("Reformat — LLM", spec, payload.Text, null, processLog, null, null);
            MessageBox.Show($"Reformat failed:\n{result.FailureMessage}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        context.SuppressNextClipboardChange();
        ClipboardWriter.SetText(result.Output);
        ActionLog.Write("Reformat — LLM", spec, payload.Text, null, processLog, result.Output, null);
    }
}
