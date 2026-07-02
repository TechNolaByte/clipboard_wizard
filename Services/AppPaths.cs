using System.IO;

namespace ClipboardWizard.Services;

/// <summary>
/// Well-known filesystem locations. Bundled media tools live in a gitignored <c>library-dump</c>
/// folder inside the project directory (kept off PATH so they don't collide with other apps).
/// Temp working directories are used to run the <c>claude</c> CLI in a neutral location where no
/// project CLAUDE.md is auto-discovered (faster, and the reformat prompt stays uninfluenced).
/// </summary>
public static class AppPaths
{
    /// <summary>The project directory (where ClipboardWizard.csproj lives), resolved at runtime.</summary>
    public static string Root { get; } = ResolveProjectDir();

    /// <summary>Downloaded ffmpeg/ImageMagick binaries land here. Gitignored, off PATH.</summary>
    public static string LibraryDump => EnsureDir(Path.Combine(Root, "library-dump"));

    private static string TempRoot => EnsureDir(Path.Combine(Path.GetTempPath(), "ClipboardWizard"));

    /// <summary>An empty working dir for fast, uninfluenced <c>claude -p</c> text runs.</summary>
    public static string NeutralDir => EnsureDir(Path.Combine(TempRoot, "neutral"));

    /// <summary>Scratch dir for image inputs/outputs handed to CLI tools and to <c>claude</c> Read.</summary>
    public static string WorkDir => EnsureDir(Path.Combine(TempRoot, "work"));

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
