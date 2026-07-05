using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;

namespace ClipboardWizard.Commands;

public enum SearchEngine
{
    /// <summary>Plain Google web search.</summary>
    Google,

    /// <summary>Perplexity AI answer engine.</summary>
    Perplexity,
}

/// <summary>
/// Opens the clipboard text as a query in the default browser — Google or Perplexity. The clipboard
/// is left untouched; this just launches a browser tab, so it's read-only (no suppression needed).
/// </summary>
public sealed class SearchOnlineCommand : IClipboardCommand
{
    private readonly SearchEngine _engine;

    public SearchOnlineCommand(SearchEngine engine) => _engine = engine;

    public string Name => _engine == SearchEngine.Google
        ? "Search online — Google"
        : "Search online — Perplexity";

    public CommandCategory Category => CommandCategory.Action;

    // Search the copied text. Files (no text) aren't meaningful web queries, so gate on text only.
    public bool CanExecute(ClipboardPayload payload) => payload.HasText;

    public Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var query = payload.Text?.Trim();
        if (string.IsNullOrEmpty(query))
            return Task.CompletedTask;

        var encoded = Uri.EscapeDataString(query);
        var url = _engine == SearchEngine.Google
            ? "https://www.google.com/search?q=" + encoded
            : "https://www.perplexity.ai/search/new?q=" + encoded;

        try
        {
            // UseShellExecute launches the user's default browser for the URL.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            ActionLog.Write(Name, $"Open {_engine} search for the clipboard text",
                query, null, $"Opened {url}",
                "(clipboard unchanged)", null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open a browser:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }
}
