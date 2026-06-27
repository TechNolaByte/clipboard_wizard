using ClipboardWizard.Models;

namespace ClipboardWizard.UI;

/// <summary>View-model wrapper that gives the list a display string and a stable group/order.</summary>
public sealed class CommandItem
{
    public required IClipboardCommand Command { get; init; }

    /// <summary>Original position in the registry list; used to keep grouping order stable.</summary>
    public required int Index { get; init; }

    public string Display => Command.Name;

    public string Group => Command.Category switch
    {
        CommandCategory.PythonScript => "Scripts",
        CommandCategory.Image => "Image",
        _ => "Actions",
    };

    public int GroupOrder => Command.Category switch
    {
        CommandCategory.PythonScript => 0,
        CommandCategory.Image => 1,
        _ => 2,
    };
}
