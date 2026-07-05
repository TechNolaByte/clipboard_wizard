using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Ask Claude": like <see cref="ActWithCommand"/> but with no instruction dialog — it opens an
/// interactive Claude Code session in a terminal immediately, seeded with a fixed "tell me about this
/// clipboard context" prompt plus the clipboard content. This is the popup's default action (top of
/// the Research group), so pressing Enter with no other input runs it.
/// </summary>
public sealed class AskClaudeCommand : IClipboardCommand
{
    private const string Instruction = "Tell me about this clipboard context:";

    public string Name => "Ask Claude";

    public CommandCategory Category => CommandCategory.Research;

    public bool CanExecute(ClipboardPayload payload) => payload.HasInput || payload.HasImage;

    public Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        string content;
        try
        {
            content = ClaudeSession.StageClipboard(payload);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't prepare the clipboard content:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return Task.CompletedTask;
        }

        try
        {
            ClaudeSession.Launch("askclaude", Instruction, content);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open a Claude terminal:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return Task.CompletedTask;
        }

        ActionLog.Write("Ask Claude", Instruction, content, null,
            "Opened an interactive Claude Code session in a terminal (normal permissions).",
            "(interactive — see the terminal)", null);

        return Task.CompletedTask;
    }
}
