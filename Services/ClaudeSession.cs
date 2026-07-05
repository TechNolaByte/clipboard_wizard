using System.IO;
using System.Linq;
using System.Text;
using ClipboardWizard.Models;

namespace ClipboardWizard.Services;

/// <summary>
/// Shared plumbing for the commands that open an interactive Claude Code terminal on the clipboard
/// (<c>Act with…</c> and <c>Ask Claude</c>): it stages the clipboard to a compact string, then writes
/// the prompt plus a wrapper script and launches a terminal. The wrapper reads the prompt back from a
/// file so no multi-line/quoted content rides on Tabby's argument parser.
/// </summary>
public static class ClaudeSession
{
    /// <summary>Return a compact description of the clipboard, staging large text/images to files.</summary>
    public static string StageClipboard(ClipboardPayload payload)
    {
        if (payload.HasText)
        {
            if (payload.Text!.Length <= 4000)
                return payload.Text!;
            var staged = Path.Combine(AppPaths.ScratchpadDir, $"clipboard_{Guid.NewGuid():N}.txt");
            File.WriteAllText(staged, payload.Text!);
            return $"(large clipboard text saved to {staged} — read that file)";
        }

        if (payload.HasFiles)
            return "Files on the clipboard:\n" + string.Join('\n', payload.Files!.Select(f => "- " + f));

        if (payload.HasImage)
        {
            var staged = ImageIO.SavePng(payload.Image!, AppPaths.ScratchpadDir);
            return $"(clipboard image saved to {staged} — read that file to view it)";
        }

        return "(clipboard is empty)";
    }

    /// <summary>
    /// Launch an interactive Claude Code terminal (normal permissions) seeded with the instruction and
    /// the already-staged clipboard content. <paramref name="filePrefix"/> just names the temp files.
    /// </summary>
    public static void Launch(string filePrefix, string instruction, string content)
    {
        var prompt = $"{instruction}\n\n--- Clipboard content ---\n{content}";

        var promptFile = Path.Combine(AppPaths.ScratchpadDir, $"{filePrefix}_{Guid.NewGuid():N}.txt");
        File.WriteAllText(promptFile, prompt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // Wrapper reads the prompt from the file and passes it to claude as a single argument, so
        // nothing complex rides on the command line. No -p / no bypass → interactive, follows permissions.
        var wrapper = Path.Combine(AppPaths.ScratchpadDir, $"{filePrefix}_{Guid.NewGuid():N}.ps1");
        var script =
            $"Set-Location -LiteralPath '{Esc(AppPaths.WorkingRoot)}'\n" +
            $"& '{Esc(ClaudeCli.Executable)}' --model sonnet ([System.IO.File]::ReadAllText('{Esc(promptFile)}'))\n";
        File.WriteAllText(wrapper, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Terminal.RunScript(wrapper);
    }

    private static string Esc(string s) => s.Replace("'", "''");
}
