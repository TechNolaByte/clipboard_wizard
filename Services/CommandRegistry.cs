using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClipboardWizard.Commands;
using ClipboardWizard.Models;

namespace ClipboardWizard.Services;

/// <summary>
/// Produces the list of commands shown in the popup: the user's Python scripts (newest first),
/// then image operations (when the clipboard holds an image), then the built-in actions.
/// This is the single place to register new commands.
/// </summary>
public sealed class CommandRegistry
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff" };

    private readonly string _scriptsDir;

    public CommandRegistry(string scriptsDir)
    {
        _scriptsDir = scriptsDir;
        EnsureScriptsFolder();
    }

    public string ScriptsDirectory => _scriptsDir;

    /// <summary>Returns the commands applicable to <paramref name="payload"/>, in display order.</summary>
    public IReadOnlyList<IClipboardCommand> GetCommands(ClipboardPayload payload)
    {
        var commands = new List<IClipboardCommand>();
        commands.AddRange(GetPythonScripts());
        commands.AddRange(GetImageCommands());
        commands.AddRange(GetActions());
        return commands.Where(c => c.CanExecute(payload)).ToList();
    }

    private IEnumerable<IClipboardCommand> GetPythonScripts()
    {
        if (!Directory.Exists(_scriptsDir))
            return Enumerable.Empty<IClipboardCommand>();

        return new DirectoryInfo(_scriptsDir)
            .EnumerateFiles("*.py")
            .OrderByDescending(f => f.LastWriteTimeUtc) // most recently edited on top
            .Select(f => (IClipboardCommand)new PythonScriptCommand(f.FullName))
            .ToList();
    }

    // Image operations. The deterministic conversions (gif<->png, jpg->png) are local; the AI ones
    // (transcribe/describe) will call the cheapest available vision model. All stubbed for now and
    // gated so they only appear for image payloads.
    private static IEnumerable<IClipboardCommand> GetImageCommands()
    {
        yield return new NotImplementedCommand("Split GIF into PNGs", CommandCategory.Image, HasGif);
        yield return new NotImplementedCommand("Join PNGs into GIF", CommandCategory.Image, HasMultipleImageFiles);
        yield return new NotImplementedCommand(".jpg to .png", CommandCategory.Image, HasJpeg);
        yield return new NotImplementedCommand("Transcribe (AI)", CommandCategory.Image, HasAnyImage);
        yield return new NotImplementedCommand("Describe — short (AI)", CommandCategory.Image, HasAnyImage);
        yield return new NotImplementedCommand("Describe — medium (AI)", CommandCategory.Image, HasAnyImage);
        yield return new NotImplementedCommand("Describe — long (AI)", CommandCategory.Image, HasAnyImage);
    }

    private static IEnumerable<IClipboardCommand> GetActions()
    {
        // Implemented today:
        yield return new PowerShellCommand();

        // On the roadmap — visible stubs so the menu reflects the full plan:
        yield return new NotImplementedCommand("Execute on Tailscale peer");
        yield return new NotImplementedCommand("Execute on all computers");
        yield return new NotImplementedCommand("Log to Obsidian daily journal");
        yield return new NotImplementedCommand("Clipboard Hawk (record stack)");
        yield return new NotImplementedCommand("Cycle Clipboard (fragment + paste)");
        yield return new NotImplementedCommand("Intelligent Reformat (Claude)");
        yield return new NotImplementedCommand("Auto-format and print");
    }

    // ---- payload predicates ----
    private static bool HasAnyImage(ClipboardPayload p) => p.HasImage || HasImageFiles(p, 1);

    private static bool HasGif(ClipboardPayload p) =>
        p.Files?.Any(f => string.Equals(Path.GetExtension(f), ".gif", StringComparison.OrdinalIgnoreCase)) == true;

    private static bool HasJpeg(ClipboardPayload p) =>
        p.HasImage
        || p.Files?.Any(f => Path.GetExtension(f) is var ext
            && (string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase))) == true;

    private static bool HasMultipleImageFiles(ClipboardPayload p) => HasImageFiles(p, 2);

    private static bool HasImageFiles(ClipboardPayload p, int min) =>
        (p.Files?.Count(f => ImageExtensions.Contains(Path.GetExtension(f))) ?? 0) >= min;

    private void EnsureScriptsFolder()
    {
        Directory.CreateDirectory(_scriptsDir);

        // Seed a couple of examples on first run so the Scripts group isn't empty and the
        // expected stdin -> stdout contract is obvious.
        if (Directory.EnumerateFiles(_scriptsDir, "*.py").Any())
            return;

        File.WriteAllText(Path.Combine(_scriptsDir, "uppercase.py"),
            "import sys\n" +
            "data = sys.stdin.read()\n" +
            "sys.stdout.write(data.upper())\n");

        File.WriteAllText(Path.Combine(_scriptsDir, "collapse_whitespace.py"),
            "import sys, re\n" +
            "data = sys.stdin.read()\n" +
            "sys.stdout.write(re.sub(r'\\s+', ' ', data).strip())\n");
    }
}
