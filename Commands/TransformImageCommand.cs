using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Transform image": describe a transformation in plain language; Sonnet emits the exact
/// command-line arguments for the bundled image tool (ImageMagick if present, otherwise ffmpeg),
/// which is run on the clipboard image. The result is placed back on the clipboard.
/// </summary>
public sealed class TransformImageCommand : IClipboardCommand
{
    public string Name => "Transform image";

    public CommandCategory Category => CommandCategory.Image;

    public bool CanExecute(ClipboardPayload payload) =>
        payload.HasImage || (payload.Files?.Any(ImageIO.IsImageFile) ?? false);

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var spec = Prompts.AskText("Transform image",
            "Describe the transformation, e.g. 'resize to 800px wide', 'convert to grayscale', " +
            "'rotate 90° clockwise', 'crop to a centered square'.",
            context: "Applies to the clipboard image. Claude receives your description (not the image " +
                     "itself) and returns command-line arguments for the bundled image tool, which is " +
                     "then run on the image.");
        if (string.IsNullOrWhiteSpace(spec))
            return;

        string inPath;
        try
        {
            inPath = ImageIO.Materialize(payload, AppPaths.ScratchpadDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No image to transform:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string tool, toolName;
        try
        {
            var magick = await MediaTools.TryEnsureMagickAsync();
            if (magick is not null)
            {
                tool = magick;
                toolName = "magick";
            }
            else
            {
                tool = await MediaTools.EnsureFfmpegAsync();
                toolName = "ffmpeg";
            }
        }
        catch (OperationCanceledException)
        {
            return; // user declined the download
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't prepare an image tool:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var outPath = Path.Combine(AppPaths.ScratchpadDir, $"out_{Guid.NewGuid():N}.png");

        var systemPrompt =
            $"Output ONLY a single line: the command-line arguments to pass to the {toolName} " +
            "executable to perform the described image transformation. Use the EXACT tokens {IN} " +
            "and {OUT} where the input and output file paths belong. Do NOT include the executable " +
            $"name itself, no quotes around the whole line, no commentary, and no markdown. For " +
            $"{toolName}, produce valid arguments (e.g. for magick: '{{IN}} -resize 800x {{OUT}}'; " +
            "for ffmpeg: '-i {IN} -vf scale=800:-1 {OUT}').";

        if (AppState.Verbose)
        {
            VerboseRunner.Run("Transform image (derive args)", ClaudeCli.Executable,
                ClaudeCli.TextArgs(spec, systemPrompt), null);
            return;
        }

        ClaudeResult gen;
        StatusToast.Show("Transform image · Claude deriving args…");
        try
        {
            gen = await ClaudeCli.RunTextAsync(spec, null, systemPrompt);
        }
        catch (Exception ex)
        {
            StatusToast.Hide();
            MessageBox.Show($"Couldn't run claude:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        StatusToast.Hide();

        if (!gen.Success || string.IsNullOrWhiteSpace(gen.Output))
        {
            MessageBox.Show($"Couldn't derive the transform:\n{gen.FailureMessage}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var args = BuildArgs(gen.Output, toolName, inPath, outPath);

        ProcResult run;
        StatusToast.Show($"Transform image · running {toolName}…");
        try
        {
            run = await Proc.RunAsync(tool, args, workingDir: AppPaths.ScratchpadDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't run {toolName}:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            StatusToast.Hide();
        }

        var processLog =
            $"tool: {toolName}\ngenerated args: {gen.Output}\nfinal args: {string.Join(' ', args)}\n\n" +
            $"exit code: {run.ExitCode}\nstdout:\n{run.StdOut}\nstderr:\n{run.StdErr}";

        if (!run.Ok || !File.Exists(outPath))
        {
            ActionLog.Write("Transform image", spec, null, inPath, processLog, null, null);
            MessageBox.Show(
                $"{toolName} failed (exit {run.ExitCode}).\n\nArguments:\n{string.Join(' ', args)}\n\n{run.StdErr}",
                "Clipboard Wizard", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var image = ImageIO.Load(outPath);
            context.SuppressNextClipboardChange();
            ClipboardWriter.SetImage(image);
            ActionLog.Write("Transform image", spec, null, inPath, processLog, null, outPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Transform ran but the result couldn't be loaded:\n{ex.Message}",
                "Clipboard Wizard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Substitute the path tokens and tokenize into an argument list.</summary>
    private static List<string> BuildArgs(string line, string toolName, string inPath, string outPath)
    {
        var tokens = Tokenize(line.Trim());

        // Drop a leading executable name if the model included one anyway.
        if (tokens.Count > 0 &&
            (tokens[0].Equals(toolName, StringComparison.OrdinalIgnoreCase) ||
             tokens[0].Equals(toolName + ".exe", StringComparison.OrdinalIgnoreCase)))
            tokens.RemoveAt(0);

        return tokens
            .Select(t => t.Replace("{IN}", inPath).Replace("{OUT}", outPath))
            .ToList();
    }

    /// <summary>Split a command line into tokens, honouring double quotes.</summary>
    private static List<string> Tokenize(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(ch);
            }
        }
        if (sb.Length > 0)
            result.Add(sb.ToString());

        return result;
    }
}
