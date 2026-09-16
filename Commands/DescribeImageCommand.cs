using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

public enum DescribeMode
{
    /// <summary>A concise ~5-word title.</summary>
    Title,

    /// <summary>A ~3-sentence description.</summary>
    Verbose,

    /// <summary>The exact text contained in the image, transcribed verbatim (OCR).</summary>
    Transcribe,

    /// <summary>
    /// The Title prompt, but the result becomes a file name: the image is saved to Downloads as
    /// "{yyyy-MM-dd HH-mm-ss} {title}.{ext}" instead of the title going to the clipboard.
    /// </summary>
    SaveWithName,
}

/// <summary>
/// Describe a clipboard image with Sonnet's vision. The CLI has no image flag, so the image is
/// materialised to a temp file and viewed through the Read tool. The description is copied to the
/// clipboard and also shown.
/// </summary>
public sealed class DescribeImageCommand : IClipboardCommand
{
    private readonly DescribeMode _mode;

    public DescribeImageCommand(DescribeMode mode) => _mode = mode;

    public string Name => _mode switch
    {
        DescribeMode.Title => "Describe image — title",
        DescribeMode.Verbose => "Describe image — verbose",
        DescribeMode.SaveWithName => "Save file with intelligent name",
        _ => "Transcribe — exact text in image",
    };

    public CommandCategory Category => CommandCategory.Image;

    public bool CanExecute(ClipboardPayload payload) =>
        payload.HasImage || (payload.Files?.Any(ImageIO.IsImageFile) ?? false);

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        string imagePath;
        try
        {
            imagePath = ImageIO.Materialize(payload, AppPaths.ScratchpadDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No image to describe:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var instruction = _mode switch
        {
            DescribeMode.Title =>
                $"View the image file at {imagePath} and give it a concise title of about 5 words. " +
                "Output only the title, nothing else.",
            DescribeMode.Verbose =>
                $"View the image file at {imagePath} and describe it in about 3 sentences. " +
                "Output only the description, nothing else.",
            DescribeMode.SaveWithName => IntelligentName.TitleInstruction(imagePath),
            _ =>
                $"View the image file at {imagePath} and transcribe the exact text it contains, " +
                "verbatim, preserving line breaks and reading order. Output only the transcribed text " +
                "and nothing else. If the image contains no text, output nothing.",
        };

        if (AppState.Verbose)
        {
            VerboseRunner.Run(Name, ClaudeCli.Executable,
                ClaudeCli.VisionArgs(instruction, AppPaths.ScratchpadDir), null);
            return;
        }

        ClaudeResult result;
        StatusToast.Show($"{Name} · Claude processing…");
        try
        {
            result = await ClaudeCli.RunVisionReadAsync(instruction, AppPaths.ScratchpadDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't run claude:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            StatusToast.Hide();
        }

        var processLog = $"claude stdout:\n{result.Output}\n\nstderr:\n{result.Error}";
        if (!result.Success || string.IsNullOrEmpty(result.Output))
        {
            ActionLog.Write(Name, instruction, null, imagePath, processLog, null, null);
            MessageBox.Show($"Describe failed:\n{result.FailureMessage}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_mode == DescribeMode.SaveWithName)
        {
            // Nothing touches the clipboard here: the materialised image goes to Downloads under
            // "{now} {title}" and is revealed in Explorer. A clipboard bitmap was encoded as PNG by
            // Materialize; a file payload keeps its own extension.
            var ext = payload.HasImage ? ".png" : Path.GetExtension(imagePath);
            var stem = $"{IntelligentName.Stamp(DateTime.Now)} {IntelligentName.Sanitize(result.Output)}";
            string dest;
            try
            {
                dest = IntelligentName.UniquePath(IntelligentName.DownloadsDir(), stem, ext);
                File.Copy(imagePath, dest);
            }
            catch (Exception ex)
            {
                ActionLog.Write(Name, instruction, null, imagePath, processLog, result.Output, null);
                MessageBox.Show($"Couldn't save to Downloads:\n{ex.Message}", "Clipboard Wizard",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            ActionLog.Write(Name, instruction, null, imagePath, processLog, Path.GetFileName(dest), dest);
            JpgToPngCommand.RevealInExplorer(dest);
            return;
        }

        context.SuppressNextClipboardChange();
        ClipboardWriter.SetText(result.Output);
        ActionLog.Write(Name, instruction, null, imagePath, processLog, result.Output, null);
        Prompts.ShowResult($"{Name} (copied to clipboard)", result.Output);
    }
}
