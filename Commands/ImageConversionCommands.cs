using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardWizard.Models;
using ClipboardWizard.Services;

namespace ClipboardWizard.Commands;

/// <summary>
/// ".jpg → .png": convert each JPEG file on the clipboard to a PNG written beside it. Uses WPF's
/// built-in codecs — no external tool needed.
/// </summary>
public sealed class JpgToPngCommand : IClipboardCommand
{
    public string Name => ".jpg → .png";

    public CommandCategory Category => CommandCategory.Image;

    public bool CanExecute(ClipboardPayload payload) =>
        payload.Files?.Any(IsJpeg) ?? false;

    public Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var jpegs = payload.Files!.Where(IsJpeg).ToList();
        var written = new List<string>();

        try
        {
            foreach (var jpg in jpegs)
            {
                var pngPath = Path.ChangeExtension(jpg, ".png");
                var src = ImageIO.Load(jpg);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                using (var fs = File.Create(pngPath))
                    enc.Save(fs);
                written.Add(pngPath);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Conversion failed:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return Task.CompletedTask;
        }

        var list = string.Join('\n', written);
        ActionLog.Write(Name, "Convert JPEG → PNG (native WPF codecs)",
            string.Join('\n', jpegs), null, $"wrote:\n{list}", list, null);

        if (written.Count > 0)
            RevealInExplorer(written[0]);
        return Task.CompletedTask;
    }

    private static bool IsJpeg(string path) =>
        Path.GetExtension(path) is var e &&
        (e.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
         e.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));

    internal static void RevealInExplorer(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { /* best effort */ }
    }
}

/// <summary>"Split GIF into PNGs": extract every frame of a GIF file into a sibling folder via ffmpeg.</summary>
public sealed class SplitGifCommand : IClipboardCommand
{
    public string Name => "Split GIF into PNGs";

    public CommandCategory Category => CommandCategory.Image;

    public bool CanExecute(ClipboardPayload payload) =>
        payload.Files?.Any(f => Path.GetExtension(f).Equals(".gif", StringComparison.OrdinalIgnoreCase)) ?? false;

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var gif = payload.Files!.First(f => Path.GetExtension(f).Equals(".gif", StringComparison.OrdinalIgnoreCase));

        string ffmpeg;
        try { ffmpeg = await MediaTools.EnsureFfmpegAsync(); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't prepare ffmpeg:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var outDir = Path.Combine(
            Path.GetDirectoryName(gif)!,
            Path.GetFileNameWithoutExtension(gif) + "_frames");
        Directory.CreateDirectory(outDir);

        var args = new[] { "-y", "-i", gif, Path.Combine(outDir, "frame_%03d.png") };
        var run = await Proc.RunAsync(ffmpeg, args);

        if (!run.Ok)
        {
            MessageBox.Show($"ffmpeg failed (exit {run.ExitCode}):\n{run.StdErr}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var frames = Directory.EnumerateFiles(outDir, "frame_*.png").ToList();
        ActionLog.Write(Name, $"Split GIF into PNGs ({gif})", gif, null,
            $"exit code: {run.ExitCode}\noutput dir: {outDir}\nframes: {frames.Count}\n\nffmpeg stderr:\n{run.StdErr}",
            string.Join('\n', frames), null);

        if (frames.Count > 0)
            JpgToPngCommand.RevealInExplorer(frames[0]);
    }
}

/// <summary>"Join PNGs into GIF": combine the selected image files into an animated GIF via ffmpeg.</summary>
public sealed class JoinPngsToGifCommand : IClipboardCommand
{
    public string Name => "Join PNGs into GIF";

    public CommandCategory Category => CommandCategory.Image;

    public bool CanExecute(ClipboardPayload payload) =>
        (payload.Files?.Count(ImageIO.IsImageFile) ?? 0) >= 2;

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var frames = payload.Files!.Where(ImageIO.IsImageFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        string ffmpeg;
        try { ffmpeg = await MediaTools.EnsureFfmpegAsync(); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't prepare ffmpeg:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // ffmpeg's image-sequence input needs consecutively-numbered files, so stage copies.
        var stageDir = Path.Combine(AppPaths.ScratchpadDir, $"gifjoin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stageDir);
        try
        {
            for (var i = 0; i < frames.Count; i++)
                File.Copy(frames[i], Path.Combine(stageDir, $"frame_{i:D4}.png"), overwrite: true);

            var outGif = Path.Combine(Path.GetDirectoryName(frames[0])!, "joined.gif");
            var args = new[]
            {
                "-y", "-framerate", "10",
                "-i", Path.Combine(stageDir, "frame_%04d.png"),
                outGif,
            };
            var run = await Proc.RunAsync(ffmpeg, args);

            if (!run.Ok || !File.Exists(outGif))
            {
                ActionLog.Write(Name, "Join PNGs into GIF", string.Join('\n', frames), null,
                    $"exit code: {run.ExitCode}\nffmpeg stderr:\n{run.StdErr}", null, null);
                MessageBox.Show($"ffmpeg failed (exit {run.ExitCode}):\n{run.StdErr}", "Clipboard Wizard",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ActionLog.Write(Name, "Join PNGs into GIF", string.Join('\n', frames), null,
                $"exit code: {run.ExitCode}\noutput: {outGif}\n\nffmpeg stderr:\n{run.StdErr}", null, outGif);
            JpgToPngCommand.RevealInExplorer(outGif);
        }
        finally
        {
            try { Directory.Delete(stageDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
