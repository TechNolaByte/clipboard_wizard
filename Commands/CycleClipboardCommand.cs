using System.Threading.Tasks;
using ClipboardWizard.Models;
using ClipboardWizard.Services;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Cycle Clipboard": split the clipboard text into fragments (non-empty lines); the first is placed
/// on the clipboard and each Ctrl+V advances to the next. Stop from the tray or by copying something
/// else. Needs at least a couple of lines to be useful.
/// </summary>
public sealed class CycleClipboardCommand : IClipboardCommand
{
    public string Name => "Cycle Clipboard (fragment + paste)";

    public CommandCategory Category => CommandCategory.Action;

    public bool CanExecute(ClipboardPayload payload) => payload.HasInput;

    public Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var text = payload.PrimaryText;
        if (!string.IsNullOrEmpty(text))
            CycleClipboard.Start(text);
        return Task.CompletedTask;
    }
}
