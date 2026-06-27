using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;

namespace ClipboardWizard.Commands;

/// <summary>
/// Placeholder for commands on the roadmap. Shows up in the popup and runs the same pipeline
/// as a real command, but just reports that it's not wired up yet. Each of these is meant to be
/// swapped for a concrete <see cref="IClipboardCommand"/> as the feature lands.
/// The optional <c>canExecute</c> predicate lets a stub be gated to the right payload type
/// (e.g. image-only commands) so the menu stays relevant.
/// </summary>
public sealed class NotImplementedCommand : IClipboardCommand
{
    private readonly Func<ClipboardPayload, bool> _canExecute;

    public NotImplementedCommand(
        string name,
        CommandCategory category = CommandCategory.Action,
        Func<ClipboardPayload, bool>? canExecute = null)
    {
        Name = name;
        Category = category;
        _canExecute = canExecute ?? (_ => true);
    }

    public string Name { get; }

    public CommandCategory Category { get; }

    public bool CanExecute(ClipboardPayload payload) => _canExecute(payload);

    public Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        MessageBox.Show($"'{Name}' isn't implemented yet.", "Clipboard Wizard",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }
}
