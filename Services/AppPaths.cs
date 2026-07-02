using System.IO;

namespace ClipboardWizard.Services;

/// <summary>
/// Well-known filesystem locations.
///
/// The <c>working/</c> directory lives inside the project and is the working area for all AI/terminal
/// work: the <c>claude</c> CLI and the "Execute as PowerShell" terminal both cd into it, so Claude
/// discovers the in-situ memory (<c>working/CLAUDE.md</c> → <c>working/config/</c>) on every run.
///
///   working/
///     CLAUDE.md    - memory instructions (points Claude at ./config)
///     config/      - durable project memory Claude keeps in situ (tracked)
///     scratchpad/  - transient staging (images, large payloads) — gitignored
///     logs/        - per-action .rtf audit logs — gitignored
///
/// Bundled media binaries live in a separate gitignored <c>library-dump/</c>, off PATH.
/// </summary>
public static class AppPaths
{
    /// <summary>The project directory (where ClipboardWizard.csproj lives), resolved at runtime.</summary>
    public static string Root { get; } = ResolveProjectDir();

    /// <summary>Downloaded ffmpeg/ImageMagick binaries land here. Gitignored, off PATH.</summary>
    public static string LibraryDump => EnsureDir(Path.Combine(Root, "library-dump"));

    /// <summary>The working area Claude and the terminal cd into for all work.</summary>
    public static string WorkingRoot => EnsureDir(Path.Combine(Root, "working"));

    /// <summary>Durable project memory Claude keeps in situ.</summary>
    public static string ConfigDir => EnsureDir(Path.Combine(WorkingRoot, "config"));

    /// <summary>Transient staging for images and large clipboard payloads (gitignored).</summary>
    public static string ScratchpadDir => EnsureDir(Path.Combine(WorkingRoot, "scratchpad"));

    /// <summary>Per-action .rtf audit logs (gitignored — they contain raw clipboard data).</summary>
    public static string LogsDir => EnsureDir(Path.Combine(WorkingRoot, "logs"));

    /// <summary>
    /// Create the working scaffold and seed <c>working/CLAUDE.md</c> if absent. Called at startup.
    /// </summary>
    public static void InitializeWorkingArea()
    {
        _ = ConfigDir;
        _ = ScratchpadDir;
        _ = LogsDir;

        var claudeMd = Path.Combine(WorkingRoot, "CLAUDE.md");
        if (!File.Exists(claudeMd))
            File.WriteAllText(claudeMd, SeedClaudeMd);
    }

    private const string SeedClaudeMd =
        "# Clipboard Wizard — working directory\n\n" +
        "This folder is the working directory for Clipboard Wizard's AI actions. The app runs the\n" +
        "`claude` CLI here with a task derived from the clipboard, and the \"Execute as PowerShell\"\n" +
        "command opens a terminal here.\n\n" +
        "## Memory (keep it in situ)\n" +
        "- Persist durable project notes/memory as markdown files in `./config/`, and read them at\n" +
        "  the start of each task so context carries across runs.\n" +
        "- One fact per file; update an existing note rather than duplicating; delete notes that\n" +
        "  turn out to be wrong.\n\n" +
        "## Layout\n" +
        "- `./config/`     — durable memory (this is where your notes live).\n" +
        "- `./scratchpad/` — transient working files (staged images, large payloads); safe to clobber.\n" +
        "- `./logs/`       — per-action `.rtf` audit logs written by the app; do not edit.\n";

    private static string ResolveProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("ClipboardWizard.csproj").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
