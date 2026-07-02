using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using ClipboardWizard.Models;

namespace ClipboardWizard.Services;

/// <summary>Helpers for moving images between the clipboard payload, disk, and BitmapSource.</summary>
public static class ImageIO
{
    private static readonly string[] ImageExts =
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff" };

    public static bool IsImageFile(string path) =>
        ImageExts.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Encode a BitmapSource to a PNG file in <paramref name="dir"/> and return its path.</summary>
    public static string SavePng(BitmapSource src, string dir)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"clip_{Guid.NewGuid():N}.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }

    /// <summary>Load an image file into a frozen, file-unlocked BitmapSource.</summary>
    public static BitmapSource Load(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Materialize the payload's image into <paramref name="dir"/> as a real file (encoding a
    /// clipboard bitmap to PNG, or copying the first image file), and return the path.
    /// </summary>
    public static string Materialize(ClipboardPayload payload, string dir)
    {
        Directory.CreateDirectory(dir);

        if (payload.HasImage)
            return SavePng(payload.Image!, dir);

        var source = payload.Files?.FirstOrDefault(IsImageFile)
            ?? throw new InvalidOperationException("No image is available on the clipboard.");
        var dest = Path.Combine(dir, Path.GetFileName(source));
        File.Copy(source, dest, overwrite: true);
        return dest;
    }
}
