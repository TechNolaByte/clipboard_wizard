using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;

namespace ClipboardWizard.Services;

/// <summary>
/// Windows' screen capture (Win+Shift+S / PrintScreen) puts the bitmap on the clipboard *and* saves a
/// file under Pictures\Screenshots — but the clipboard only carries the pixels, so pasting into a
/// terminal or an AI prompt gets you nothing. This service spots that pair and upgrades the clipboard
/// to carry the saved file as well: a file drop (Explorer, upload dialogs), the path as text
/// (terminals), and the original bitmap (image editors, chat apps). Nothing is lost — only added.
/// </summary>
public static class ScreenshotSwap
{
    // The file is written around the same time as the clipboard, but not reliably before it, so we
    // poll for a short while after the copy.
    private const int PollBudgetMs = 3000;
    private const int PollStepMs = 120;

    /// <summary>How recently a screenshot file must have been written to count as "this capture".</summary>
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(20);

    /// <summary>Guards against re-using one file for a later, unrelated image copy.</summary>
    private static DateTime _lastSwappedWriteUtc = DateTime.MinValue;

    private static string? _folder;
    private static bool _folderResolved;

    /// <summary>The Screenshots known folder (falls back to Pictures\Screenshots), or null if absent.</summary>
    public static string? Folder
    {
        get
        {
            if (_folderResolved)
                return _folder;
            _folderResolved = true;
            _folder = ResolveFolder();
            return _folder;
        }
    }

    /// <summary>
    /// True when this payload could be a fresh screen capture: a bare bitmap (no text or files of its
    /// own to clobber) and a Screenshots folder to look in.
    /// </summary>
    public static bool IsCandidate(ClipboardPayload payload) =>
        payload.HasImage && !payload.HasText && !payload.HasFiles
        && Folder is { } dir && Directory.Exists(dir);

    /// <summary>
    /// Wait briefly for the screenshot file matching <paramref name="payload"/>'s bitmap to appear,
    /// then rewrite the clipboard with the file added. Returns the upgraded payload, or null if no
    /// matching file showed up (an ordinary image copy) — in which case the clipboard is untouched.
    /// Must be called on the UI (STA) thread.
    /// </summary>
    public static async Task<ClipboardPayload?> TrySwapAsync(ClipboardPayload payload, Action suppressNextClipboardChange)
    {
        if (!IsCandidate(payload))
            return null;

        var watchingSince = DateTime.UtcNow;
        var deadline = Environment.TickCount64 + PollBudgetMs;
        string? path;
        while ((path = FindMatchingFile(payload.Image!, watchingSince)) is null)
        {
            if (Environment.TickCount64 >= deadline)
                return null;
            await Task.Delay(PollStepMs);
        }

        // Quote like Explorer's "Copy as path" does, but only when it's needed — screenshot names
        // contain spaces, so an unquoted path would break as a terminal argument.
        var pathText = path.Contains(' ') ? $"\"{path}\"" : path;

        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path });
        data.SetText(pathText);
        data.SetImage(payload.Image!);

        suppressNextClipboardChange();
        if (!TrySetClipboard(data))
            return null;

        _lastSwappedWriteUtc = SafeWriteTimeUtc(path);
        return new ClipboardPayload { Text = pathText, Image = payload.Image, Files = new[] { path } };
    }

    /// <summary>
    /// The newest freshly-written screenshot belonging to this capture, or null. Two things qualify a
    /// file, and between them they're what keeps an unrelated image copy from picking up a screenshot
    /// taken moments earlier: its pixel dimensions match the clipboard bitmap, or it was created after
    /// we started watching (so it can only be this capture — this covers a region snip whose saved
    /// "original" is the whole screen and therefore doesn't match).
    /// </summary>
    private static string? FindMatchingFile(System.Windows.Media.Imaging.BitmapSource image, DateTime watchingSince)
    {
        if (Folder is not { } dir)
            return null;

        IEnumerable<FileInfo> candidates;
        try
        {
            var cutoff = DateTime.UtcNow - Freshness;
            candidates = new DirectoryInfo(dir).EnumerateFiles()
                .Where(f => ImageIO.IsImageFile(f.Name))
                .Where(f => f.LastWriteTimeUtc >= cutoff && f.LastWriteTimeUtc > _lastSwappedWriteUtc)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(4)
                .ToList();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        foreach (var file in candidates)
        {
            try
            {
                // Decoding also proves the file is finished being written; a partial one throws and a
                // later poll picks it up.
                var saved = ImageIO.Load(file.FullName);
                if ((saved.PixelWidth == image.PixelWidth && saved.PixelHeight == image.PixelHeight)
                    || file.CreationTimeUtc >= watchingSince)
                    return file.FullName;
            }
            catch
            {
                // Still being written (or not decodable) — a later poll will pick it up.
            }
        }
        return null;
    }

    /// <summary>Set the clipboard, retrying while another app holds it open.</summary>
    private static bool TrySetClipboard(DataObject data)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (COMException)
            {
                System.Threading.Thread.Sleep(40);
            }
        }
        return false;
    }

    private static DateTime SafeWriteTimeUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (IOException) { return DateTime.UtcNow; }
    }

    private static string? ResolveFolder()
    {
        // FOLDERID_Screenshots — the user may have relocated it, so ask the shell first.
        var id = new Guid("b7bede81-df94-4682-a7d8-57a52620b86f");
        var ptr = IntPtr.Zero;
        try
        {
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out ptr) == 0 && ptr != IntPtr.Zero)
            {
                var path = Marshal.PtrToStringUni(ptr);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
        }
        catch (DllNotFoundException) { /* fall through */ }
        catch (EntryPointNotFoundException) { /* fall through */ }
        finally
        {
            if (ptr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(ptr);
        }

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return string.IsNullOrEmpty(pictures) ? null : Path.Combine(pictures, "Screenshots");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
