using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ClipboardWizard.Models;

/// <summary>
/// A snapshot of the current clipboard contents. Commands decide what they can act on
/// by inspecting the Has* flags rather than touching the live clipboard.
/// </summary>
public sealed class ClipboardPayload
{
    public string? Text { get; init; }
    public BitmapSource? Image { get; init; }
    public string[]? Files { get; init; }

    public bool HasText => !string.IsNullOrEmpty(Text);
    public bool HasImage => Image is not null;
    public bool HasFiles => Files is { Length: > 0 };

    /// <summary>
    /// Reads the clipboard with a few retries. The clipboard is a shared, frequently-locked
    /// resource, so the first read right after a change often throws — we back off and retry.
    /// Must be called on an STA thread (the WPF UI thread is fine).
    /// </summary>
    public static ClipboardPayload Capture()
    {
        string? text = null;
        BitmapSource? image = null;
        string[]? files = null;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
                if (Clipboard.ContainsImage())
                    image = Clipboard.GetImage();
                if (Clipboard.ContainsFileDropList())
                    files = Clipboard.GetFileDropList().Cast<string>().ToArray();
                break;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }

        return new ClipboardPayload { Text = text, Image = image, Files = files };
    }
}
