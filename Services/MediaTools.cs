using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ClipboardWizard.UI;

namespace ClipboardWizard.Services;

/// <summary>
/// Resolves the bundled media binaries (ffmpeg, ImageMagick), downloading them on first use into
/// <see cref="AppPaths.LibraryDump"/>. They're kept off PATH and invoked by absolute path so they
/// never shadow other apps' installs.
///
/// ffmpeg has a stable download URL and is the reliable workhorse for image work. ImageMagick's
/// portable zip URL is not machine-resolvable, so <see cref="TryEnsureMagickAsync"/> is best-effort:
/// it uses a library-dump or PATH copy if present, otherwise returns null and callers fall back to
/// ffmpeg.
/// </summary>
public static class MediaTools
{
    private const string FfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>Absolute path to ffmpeg, downloading it (with the user's OK) if missing.</summary>
    public static async Task<string> EnsureFfmpegAsync(CancellationToken ct = default)
    {
        var dest = Path.Combine(AppPaths.LibraryDump, "ffmpeg", "ffmpeg.exe");
        if (File.Exists(dest))
            return dest;

        if (!Prompts.Confirm("Download ffmpeg",
                "ffmpeg isn't bundled yet (~90 MB). Download it into library-dump now?\n\n" +
                "It's stored inside the project folder and kept off your PATH."))
            throw new OperationCanceledException("ffmpeg download was declined.");

        await DownloadAndExtractExeAsync(FfmpegUrl, "ffmpeg.exe", dest, ct);
        return dest;
    }

    /// <summary>
    /// Best-effort ImageMagick resolution: library-dump copy → PATH copy → null. Returns null when
    /// ImageMagick isn't available; callers should fall back to ffmpeg and inform the user.
    /// </summary>
    public static Task<string?> TryEnsureMagickAsync(CancellationToken ct = default)
    {
        var bundled = Path.Combine(AppPaths.LibraryDump, "magick", "magick.exe");
        if (File.Exists(bundled))
            return Task.FromResult<string?>(bundled);

        var onPath = ResolveOnPath("magick.exe");
        return Task.FromResult(onPath);
    }

    private static async Task DownloadAndExtractExeAsync(string url, string exeName, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        var tempZip = Path.Combine(Path.GetTempPath(), $"clipwiz_dl_{Guid.NewGuid():N}.zip");
        var tempDir = Path.Combine(Path.GetTempPath(), $"clipwiz_ex_{Guid.NewGuid():N}");
        try
        {
            await using (var resp = await Http.GetStreamAsync(url, ct))
            await using (var fs = File.Create(tempZip))
                await resp.CopyToAsync(fs, ct);

            ZipFile.ExtractToDirectory(tempZip, tempDir);

            var found = Directory.EnumerateFiles(tempDir, exeName, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException($"'{exeName}' was not found inside the downloaded archive.");

            File.Copy(found, dest, overwrite: true);
        }
        finally
        {
            TryDelete(tempZip);
            TryDeleteDir(tempDir);
        }
    }

    private static string? ResolveOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }
        return null;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }
}
