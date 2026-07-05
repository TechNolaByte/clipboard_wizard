using System.Threading.Tasks;
using ClipboardWizard.Models;
using ClipboardWizard.Services;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Clipboard Cycle": split the clipboard text into fragments (non-empty lines); the first is placed
/// on the clipboard and each Ctrl+V advances to the next. Ends on Escape or on the next copy.
/// </summary>
public sealed class ClipboardCycleCommand : IClipboardCommand
{
    public string Name => "Clipboard Cycle (fragment + paste)";

    public CommandCategory Category => CommandCategory.Collect;

    public bool CanExecute(ClipboardPayload payload) => payload.HasInput;

    public Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var text = payload.PrimaryText;
        if (!string.IsNullOrEmpty(text))
            ClipboardCycle.Start(text);
        return Task.CompletedTask;
    }
}
