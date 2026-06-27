using System.Threading.Tasks;
using System.Windows;

namespace ClipboardWizard.Models;

public enum CommandCategory
{
    /// <summary>User-supplied Python scripts that transform the clipboard in place.</summary>
    PythonScript,

    /// <summary>Operations offered only when the clipboard holds an image or image files.</summary>
    Image,

    /// <summary>Built-in verbs (run as PowerShell, log to Obsidian, etc.).</summary>
    Action,
}

/// <summary>
/// Everything the popup can run against the current clipboard payload.
/// New commands only need to implement this interface and be returned by the registry.
/// </summary>
public interface IClipboardCommand
{
    string Name { get; }

    CommandCategory Category { get; }

    /// <summary>Whether this command is meaningful for the captured payload (e.g. needs text).</summary>
    bool CanExecute(ClipboardPayload payload);

    Task ExecuteAsync(ClipboardPayload payload, CommandContext context);
}

/// <summary>
/// Services a command may need while running. Kept tiny on purpose; grows as commands do.
/// </summary>
public sealed class CommandContext
{
    /// <summary>
    /// Call this immediately before writing to the clipboard yourself, so the monitor
    /// ignores the resulting change event instead of re-opening the popup.
    /// </summary>
    public required Action SuppressNextClipboardChange { get; init; }

    /// <summary>The popup window, for parenting dialogs.</summary>
    public Window? Owner { get; init; }
}
