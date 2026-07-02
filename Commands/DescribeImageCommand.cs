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

    public string Name => _mode == DescribeMode.Title
        ? "Describe image — title"
        : "Describe image — verbose";

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

        var instruction = _mode == DescribeMode.Title
            ? $"View the image file at {imagePath} and give it a concise title of about 5 words. " +
              "Output only the title, nothing else."
            : $"View the image file at {imagePath} and describe it in about 3 sentences. " +
              "Output only the description, nothing else.";

        ClaudeResult result;
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

        var processLog = $"claude stdout:\n{result.Output}\n\nstderr:\n{result.Error}";
        if (!result.Success || string.IsNullOrEmpty(result.Output))
        {
            ActionLog.Write(Name, instruction, null, imagePath, processLog, null, null);
            MessageBox.Show($"Describe failed:\n{result.FailureMessage}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        context.SuppressNextClipboardChange();
        ClipboardWriter.SetText(result.Output);
        ActionLog.Write(Name, instruction, null, imagePath, processLog, result.Output, null);
        Prompts.ShowResult($"{Name} (copied to clipboard)", result.Output);
    }
}
