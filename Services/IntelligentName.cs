using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.UI;

namespace ClipboardWizard.Services;

/// <summary>
/// An "intelligent name": a local timestamp plus a short vision-generated title, e.g.
/// <c>2026-09-16T07.42.10 Cat asleep on keyboard.png</c> — Duck's datetime format, with dots in
/// the clock because a colon can't be in a file name. Shared by the clipboard command
/// ("Save file with intelligent name" → Downloads) and the Explorer context-menu verb
/// (<c>--intelligent-rename</c> → selected image files renamed in place).
/// </summary>
public static class IntelligentName
{
    public const string CommandName = "Rename with intelligent name";
    public const string RenameSwitch = "--intelligent-rename";
    public const string StampFormat = "yyyy-MM-ddTHH.mm.ss";

    private static readonly Regex StampPrefix =
        new(@"^\d{4}-\d{2}-\d{2}T\d{2}\.\d{2}\.\d{2}(?:[ _-]|$)", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Stamp(DateTime when) => when.ToString(StampFormat);

    /// <summary>The vision prompt: the "describe — title" prompt, tightened for a file name.</summary>
    public static string TitleInstruction(string imagePath) =>
        $"View the image file at {imagePath} and give it a concise title of about 5 words, " +
        "suitable as a file name: plain words, no quotes, no punctuation, no file extension. " +
        "Output only the title, nothing else.";

    /// <summary>Make a model-written title safe as a file-name stem. Falls back to "image".</summary>
    public static string Sanitize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "image";

        // First line only, shorn of the quoting/markdown a model sometimes wraps a title in.
        var line = title.Trim().Split('\n')[0].Trim().Trim('"', '\'', '“', '”', '`', '*', '#', '.', ' ');

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(line.Length);
        foreach (var ch in line)
            sb.Append(invalid.Contains(ch) || char.IsControl(ch) ? ' ' : ch);

        var s = Whitespace.Replace(sb.ToString(), " ").Trim(' ', '.', '-', '_');
        if (s.Length > 80)
            s = s[..80].TrimEnd(' ', '.', '-', '_');
        return s.Length == 0 ? "image" : s;
    }

    /// <summary>The user's Downloads folder (asks the shell — it may have been relocated).</summary>
    public static string DownloadsDir()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
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

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>"{stem}{ext}" in <paramref name="dir"/>, with " (2)", " (3)"… on collision.</summary>
    public static string UniquePath(string dir, string stem, string ext)
    {
        var path = Path.Combine(dir, stem + ext);
        var i = 2;
        while (File.Exists(path))
            path = Path.Combine(dir, $"{stem} ({i++}){ext}");
        return path;
    }

    /// <summary>
    /// The stem for a file renamed in place. A file that already carries a stamp prefix keeps it
    /// (a re-run re-titles without re-dating); otherwise the stamp is the file's last-write time —
    /// for a screenshot or a photo that is when it was taken, which is the date worth keeping.
    /// </summary>
    public static string StemFor(string existingPath, string title)
    {
        var name = Path.GetFileNameWithoutExtension(existingPath);
        var stamp = StampPrefix.IsMatch(name)
            ? name[..StampFormat.Length]
            : Stamp(File.GetLastWriteTime(existingPath));
        return $"{stamp} {Sanitize(title)}";
    }

    // ---- Explorer verb: ClipboardWizard.exe --intelligent-rename "<file>" [...] -------------------

    private static readonly string SpoolDir =
        Path.Combine(Path.GetTempPath(), "ClipboardWizard.IntelligentRename");
    private const string BatchMutexName = @"Local\ClipboardWizard.IntelligentRename.v1";
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan DropMaxAge = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Explorer runs a command verb once per selected file, so a multi-select starts N processes at
    /// once. Each drops its paths into a spool; the first to take the batch mutex is the runner: it
    /// waits until the drops stop arriving, then renames the whole batch with one progress toast.
    /// The others return immediately. Drops older than a minute are stale and are discarded.
    /// </summary>
    public static async Task RunExplorerVerbAsync(IEnumerable<string> files)
    {
        Directory.CreateDirectory(SpoolDir);
        var mine = Path.Combine(SpoolDir, $"{Guid.NewGuid():N}.drop");
        File.WriteAllLines(mine, files.Where(f => f.Length > 0).Select(Path.GetFullPath));

        using var mutex = new Mutex(false, BatchMutexName);
        bool runner;
        try { runner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { runner = true; }
        if (!runner)
            return; // a sibling owns the batch and will pick our drop up

        try
        {
            while (true)
            {
                var batch = await CollectDropsAsync();
                if (batch.Count == 0)
                    break;
                await RenameInPlaceAsync(batch);
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static async Task<List<string>> CollectDropsAsync()
    {
        // Wait until a whole quiet period passes with no new drop file.
        var seen = -1;
        while (true)
        {
            await Task.Delay(QuietPeriod);
            var count = Directory.GetFiles(SpoolDir, "*.drop").Length;
            if (count == seen)
                break;
            seen = count;
        }

        var batch = new List<string>();
        foreach (var drop in Directory.GetFiles(SpoolDir, "*.drop"))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(drop) < DropMaxAge)
                    batch.AddRange(File.ReadAllLines(drop).Where(l => l.Length > 0));
                File.Delete(drop);
            }
            catch { /* still being written by a straggler — the next pass gets it */ }
        }
        return batch.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Rename each image file in place to "{stamp} {title}{ext}", one vision call each.</summary>
    public static async Task RenameInPlaceAsync(IReadOnlyList<string> files)
    {
        var images = files.Where(f => File.Exists(f) && ImageIO.IsImageFile(f)).ToList();
        if (images.Count == 0)
        {
            MessageBox.Show("No image files to rename.", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var failures = new List<string>();
        for (var i = 0; i < images.Count; i++)
        {
            var src = images[i];
            StatusToast.Show($"{CommandName} · {i + 1}/{images.Count} · {Path.GetFileName(src)}");
            try
            {
                // Stage a copy in the scratchpad so the CLI's Read stays confined to that folder.
                var staged = Path.Combine(AppPaths.ScratchpadDir, $"rename_{Guid.NewGuid():N}{Path.GetExtension(src)}");
                File.Copy(src, staged, overwrite: true);
                var instruction = TitleInstruction(staged);
                ClaudeResult result;
                try
                {
                    result = await ClaudeCli.RunVisionReadAsync(instruction, AppPaths.ScratchpadDir);
                }
                finally
                {
                    try { File.Delete(staged); } catch { /* scratch */ }
                }

                var processLog = $"claude stdout:\n{result.Output}\n\nstderr:\n{result.Error}";
                if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
                {
                    ActionLog.Write(CommandName, instruction, null, src, processLog, null, null);
                    failures.Add($"{Path.GetFileName(src)}: {result.FailureMessage}");
                    continue;
                }

                var ext = Path.GetExtension(src);
                var stem = StemFor(src, result.Output);
                if (string.Equals(stem + ext, Path.GetFileName(src), StringComparison.OrdinalIgnoreCase))
                    continue; // already carries exactly this name

                var dest = UniquePath(Path.GetDirectoryName(src)!, stem, ext);
                File.Move(src, dest);
                ActionLog.Write(CommandName, instruction, null, src, processLog, Path.GetFileName(dest), dest);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(src)}: {ex.Message}");
            }
        }
        StatusToast.Hide();

        if (failures.Count > 0)
            MessageBox.Show("Some files were not renamed:\n\n" + string.Join("\n", failures),
                "Clipboard Wizard", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
