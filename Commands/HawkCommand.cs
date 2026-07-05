using System.Threading.Tasks;
using ClipboardWizard.Models;
using ClipboardWizard.Services;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Clipboard Hawk": start recording. While active, the popup is suppressed and each copy is added
/// to a stack (with a hit sound), the count shown on the tray. Flush from the tray to join the stack
/// back onto the clipboard.
/// </summary>
public sealed class HawkCommand : IClipboardCommand
{
    public string Name => "Clipboard Hawk (record stack)";

    public CommandCategory Category => CommandCategory.Collect;

    public bool CanExecute(ClipboardPayload payload) => true;

    public Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        Hawk.Start();
        return Task.CompletedTask;
    }
}
