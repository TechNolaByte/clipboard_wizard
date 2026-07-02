using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Reformat in situ — LLM": ask for a reformat instruction, pipe the clipboard content (text, or
/// a file path for unrecognized files) through a lean, safe-mode <c>claude</c> (Sonnet) run, and
/// replace the clipboard with the result.
/// </summary>
public sealed class ReformatLlmCommand : IClipboardCommand
{
    private const string SystemPrompt =
        "You transform text. The text to transform is provided on standard input. Apply the user's " +
        "instruction and output ONLY the resulting text — no explanations, no preamble, and no " +
        "surrounding markdown code fences.";

    public string Name => "Reformat in situ — LLM";

    public CommandCategory Category => CommandCategory.Action;

    public bool CanExecute(ClipboardPayload payload) => payload.HasInput;

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var input = payload.PrimaryText;
        if (input is null)
            return;

        var spec = Prompts.AskText("Reformat — LLM",
            "How should I reformat the clipboard content?",
            context: Preview(payload, input));
        if (string.IsNullOrWhiteSpace(spec))
            return;

        if (AppState.Verbose)
        {
            VerboseRunner.Run("Reformat — LLM", ClaudeCli.Executable, ClaudeCli.TextArgs(spec, SystemPrompt), input);
            return;
        }

        ClaudeResult result;
        StatusToast.Show("Reformat — LLM · Claude processing…");
        try
        {
            result = await ClaudeCli.RunTextAsync(spec, input, SystemPrompt);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't run claude:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            StatusToast.Hide();
        }

        var processLog = $"claude stdout:\n{result.Output}\n\nstderr:\n{result.Error}";
        if (!result.Success || string.IsNullOrEmpty(result.Output))
        {
            ActionLog.Write("Reformat — LLM", spec, input, null, processLog, null, null);
            MessageBox.Show($"Reformat failed:\n{result.FailureMessage}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        context.SuppressNextClipboardChange();
        ClipboardWriter.SetText(result.Output);
        ActionLog.Write("Reformat — LLM", spec, input, null, processLog, result.Output, null);
    }

    /// <summary>A short preview of what will be sent as context, noting when it's a file path.</summary>
    internal static string Preview(ClipboardPayload payload, string input)
    {
        var header = payload.HasText ? "(clipboard text)\n" : "(file path — the file itself is not read here)\n";
        var body = input.Length > 1200 ? input[..1200] + "\n…" : input;
        return header + body;
    }
}
