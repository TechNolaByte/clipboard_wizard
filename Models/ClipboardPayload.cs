using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
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

    /// <summary>True when there's something a text/script command can act on.</summary>
    public bool HasInput => HasText || HasFiles;

    /// <summary>
    /// The input handed to text/script commands: the clipboard text if present, otherwise the
    /// file path(s) (one per line). This lets commands operate on unrecognized files by path.
    /// </summary>
    public string? PrimaryText => HasText ? Text : (HasFiles ? string.Join('\n', Files!) : null);

    /// <summary>Signature meaning "the clipboard holds nothing we'd act on".</summary>
    public const string EmptySignature = "∅";

    private string? _signature;

    /// <summary>
    /// A stable, cheap identity of the clipboard content. Re-copying the same thing bumps the OS
    /// clipboard sequence number (so we get a fresh change event) but yields the same signature — the
    /// popup uses that to distinguish a fresh copy from a deliberate re-copy. Computed lazily (image
    /// hashing isn't free) and cached, since a payload is immutable.
    /// </summary>
    public string ContentSignature => _signature ??= ComputeSignature();

    private string ComputeSignature()
    {
        if (HasText)
            return "T:" + Hash(Encoding.UTF8.GetBytes(Text!));
        if (HasFiles)
            return "F:" + Hash(Encoding.UTF8.GetBytes(string.Join('\n', Files!)));
        if (HasImage)
            return "I:" + ImageHash(Image!);
        return EmptySignature;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes));

    private static string ImageHash(BitmapSource img)
    {
        try
        {
            var bytesPerPixel = (img.Format.BitsPerPixel + 7) / 8;
            var stride = img.PixelWidth * bytesPerPixel;
            var buffer = new byte[stride * img.PixelHeight];
            img.CopyPixels(buffer, stride, 0);
            return $"{img.PixelWidth}x{img.PixelHeight}:{Hash(buffer)}";
        }
        catch
        {
            // CopyPixels can fail for exotic formats — fall back to dimensions (coarser but safe).
            return $"{img.PixelWidth}x{img.PixelHeight}:nohash";
        }
    }

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
                image = ReadImage();
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

    /// <summary>
    /// Read the clipboard image the reliable way. <c>Clipboard.GetImage()</c> decodes the CF_DIB /
    /// CF_BITMAP copy, and a 32-bpp DIB has no defined alpha: browsers, Snipping Tool and most
    /// screenshot tools leave it zero, so WPF hands back a bitmap that is entirely transparent —
    /// a blank preview and a transparent PNG on save. So: take the lossless "PNG" clipboard format
    /// first when the source offers one, and otherwise repair a dead alpha channel.
    /// </summary>
    private static BitmapSource? ReadImage()
    {
        foreach (var fmt in new[] { "PNG", "image/png" })
        {
            if (!Clipboard.ContainsData(fmt))
                continue;
            try
            {
                var data = Clipboard.GetData(fmt);
                Stream? stream = data switch
                {
                    MemoryStream ms => ms,
                    byte[] bytes => new MemoryStream(bytes),
                    Stream s => s,
                    _ => null,
                };
                if (stream is null)
                    continue;
                stream.Position = 0;
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
            catch
            {
                // a malformed PNG entry — fall through to the DIB
            }
        }

        if (!Clipboard.ContainsImage())
            return null;
        var img = Clipboard.GetImage();
        return img is null ? null : RepairDeadAlpha(img);
    }

    /// <summary>
    /// A 32-bpp bitmap whose alpha is zero on every pixel is a DIB with an unused alpha channel,
    /// not a transparent image: give it a solid alpha. Any pixel with real alpha leaves it alone.
    /// </summary>
    public static BitmapSource RepairDeadAlpha(BitmapSource img)
    {
        if (img.Format != PixelFormats.Bgra32 && img.Format != PixelFormats.Pbgra32)
            return img;
        try
        {
            int w = img.PixelWidth, h = img.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            img.CopyPixels(px, stride, 0);
            for (var i = 3; i < px.Length; i += 4)
                if (px[i] != 0)
                    return img; // real alpha somewhere — trust it
            for (var i = 3; i < px.Length; i += 4)
                px[i] = 255;
            var fixedImg = BitmapSource.Create(w, h, img.DpiX, img.DpiY, PixelFormats.Bgra32, null, px, stride);
            fixedImg.Freeze();
            return fixedImg;
        }
        catch
        {
            return img; // exotic format — better the original than nothing
        }
    }
}
