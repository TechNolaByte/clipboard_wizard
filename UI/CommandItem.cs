using ClipboardWizard.Commands;
using ClipboardWizard.Models;

namespace ClipboardWizard.UI;

/// <summary>View-model wrapper that gives the list a display string and a stable group/order.</summary>
public sealed class CommandItem
{
    public required IClipboardCommand Command { get; init; }

    /// <summary>Original position in the registry list; used to keep grouping order stable.</summary>
    public required int Index { get; init; }

    public string Display => Command.Name;

    /// <summary>True for user Python scripts, which get a delete button in the popup.</summary>
    public bool IsScript => Command is PythonScriptCommand;

    /// <summary>The .py path for a script command (null otherwise).</summary>
    public string? ScriptPath => (Command as PythonScriptCommand)?.ScriptPath;

    public string Group => Command.Category switch
    {
        CommandCategory.Research => "Research",
        CommandCategory.PythonScript => "Scripts",
        CommandCategory.Image => "Image",
        CommandCategory.Collect => "Collect",
        _ => "Actions",
    };

    public int GroupOrder => Command.Category switch
    {
        CommandCategory.Research => 0,
        CommandCategory.PythonScript => 1,
        CommandCategory.Image => 2,
        CommandCategory.Action => 3,
        CommandCategory.Collect => 4,
        _ => 3,
    };
}
