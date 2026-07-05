using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

public enum SearchEngine
{
    /// <summary>Plain Google web search (Google Lens for images).</summary>
    Google,

    /// <summary>Perplexity AI answer engine.</summary>
    Perplexity,
}

/// <summary>
/// Opens the clipboard content as a query in the default browser — Google or Perplexity.
///
/// Text is searched directly. An image is reverse-image-searched: since Google Lens / Perplexity need
/// the image at a URL but the clipboard image is local, it's uploaded to a temporary public host
/// (<see cref="ImageHost"/>) — gated behind a confirm because that puts the image on a public URL.
///
/// The clipboard is left untouched throughout; this just launches a browser tab (no suppression needed).
/// </summary>
public sealed class SearchOnlineCommand : IClipboardCommand
{
    private readonly SearchEngine _engine;

    public SearchOnlineCommand(SearchEngine engine) => _engine = engine;

    public string Name => _engine == SearchEngine.Google
        ? "Search online — Google"
        : "Search online — Perplexity";

    public CommandCategory Category => CommandCategory.Research;

    // Text searches directly; an image reverse-searches (uploaded first). Bare files aren't queries.
    public bool CanExecute(ClipboardPayload payload) =>
        payload.HasText || HasImage(payload);

    private static bool HasImage(ClipboardPayload payload) =>
        payload.HasImage || (payload.Files?.Any(ImageIO.IsImageFile) ?? false);

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        // Text wins when both are present — it's the more precise query.
        if (payload.HasText)
        {
            var query = payload.Text!.Trim();
            if (query.Length == 0)
                return;
            var encoded = Uri.EscapeDataString(query);
            var url = _engine == SearchEngine.Google
                ? "https://www.google.com/search?q=" + encoded
                : "https://www.perplexity.ai/search/new?q=" + encoded;
            OpenSearch(url, $"Open {_engine} search for the clipboard text", query);
            return;
        }

        if (HasImage(payload))
            await SearchImageAsync(payload);
    }

    private async Task SearchImageAsync(ClipboardPayload payload)
    {
        if (!Prompts.Confirm("Reverse-image search",
                $"To search this image on {_engine}, Clipboard Wizard will upload it to a temporary " +
                "public host (tmpfiles.org) to get a link, then open the results.\n\n" +
                "The image will be reachable at a public URL for about 1 hour, then auto-deleted. Continue?"))
            return;

        string imagePath;
        try
        {
            imagePath = ImageIO.Materialize(payload, AppPaths.ScratchpadDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No image to search:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string imageUrl;
        StatusToast.Show($"{Name} · uploading image…");
        try
        {
            imageUrl = await ImageHost.UploadAsync(imagePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't upload the image:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            StatusToast.Hide();
        }

        var encoded = Uri.EscapeDataString(imageUrl);
        var url = _engine == SearchEngine.Google
            // Lens takes the image URL directly and shows visual matches.
            ? "https://lens.google.com/uploadbyurl?url=" + encoded
            // Perplexity has no image-URL entry point, so ask it to look at the hosted image.
            : "https://www.perplexity.ai/search/new?q=" +
              Uri.EscapeDataString("Identify and describe this image: " + imageUrl);

        OpenSearch(url, $"Reverse-image search on {_engine} (image hosted at {imageUrl})", imageUrl);
    }

    private void OpenSearch(string url, string logInstruction, string original)
    {
        try
        {
            // UseShellExecute launches the user's default browser for the URL.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            ActionLog.Write(Name, logInstruction, original, null, $"Opened {url}",
                "(clipboard unchanged)", null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open a browser:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
