using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClipboardWizard.Services;

/// <summary>
/// Thin wrapper over the <c>claude</c> CLI. All AI features route through here rather than the
/// HTTP API, so they reuse the user's existing Claude Code credentials.
///
/// The lean clipboard ops (text transforms + vision describe) run under <c>--safe-mode</c>, which
/// disables all customizations (CLAUDE.md auto-discovery, skills, hooks, MCP, …) while keeping auth,
/// model selection, built-in tools, and permissions working — so no project docs leak into a
/// clipboard transform. Because safe-mode also skips the working memory, we dump
/// <c>working/config/</c> into the prompt manually via <see cref="DumpMemory"/>. We deliberately do
/// NOT use <c>--bare</c> (it would force API-key-only auth and ignore the OAuth login). The agentic
/// "Act with" is not lean: it runs with full context so it keeps auto-loading working memory.
/// </summary>
public static class ClaudeCli
{
    private static string? _exe;

    public static string Executable => _exe ??= ResolveExe();

    private static string ResolveExe()
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "bin", "claude.exe");
        return File.Exists(local) ? local : "claude"; // fall back to PATH
    }

    private static readonly string[] MemoryExtensions =
        { ".md", ".txt", ".json", ".yaml", ".yml", ".csv" };

    /// <summary>
    /// Read the in-situ memory (<c>working/config/</c>) into a single block for injection, since
    /// safe-mode skips CLAUDE.md auto-discovery. Returns "" when there's nothing to inject.
    /// </summary>
    private static string DumpMemory()
    {
        try
        {
            var dir = AppPaths.ConfigDir;
            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => MemoryExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            foreach (var f in files)
            {
                string content;
                try { content = File.ReadAllText(f); }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(content))
                    continue;
                sb.Append("## ").Append(Path.GetRelativePath(dir, f)).Append('\n')
                  .Append(content.Trim()).Append("\n\n");
            }

            return sb.Length == 0
                ? string.Empty
                : "# Project memory (from working/config)\n\n" + sb.ToString().TrimEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string WithMemory(string appendSystemPrompt)
    {
        var memory = DumpMemory();
        return string.IsNullOrEmpty(memory)
            ? appendSystemPrompt
            : appendSystemPrompt + "\n\n" + memory;
    }

    /// <summary>
    /// Fast, tool-less text transform. The content to operate on is piped via stdin; the user's
    /// instruction is the prompt; <paramref name="appendSystemPrompt"/> constrains output shape.
    /// Returns trimmed stdout.
    /// </summary>
    /// <summary>Args for a lean text transform (shared by the headless and verbose paths).</summary>
    public static List<string> TextArgs(string prompt, string appendSystemPrompt, string model = "sonnet") => new()
    {
        "-p", "--model", model,
        "--safe-mode",              // no CLAUDE.md/skills/hooks/MCP; auth + built-in tools stay
        "--no-session-persistence",
        "--tools", "",
        "--append-system-prompt", WithMemory(appendSystemPrompt),
        prompt,
    };

    public static async Task<ClaudeResult> RunTextAsync(
        string prompt,
        string? stdin,
        string appendSystemPrompt,
        string model = "sonnet",
        CancellationToken ct = default)
    {
        var r = await Proc.RunAsync(Executable, TextArgs(prompt, appendSystemPrompt, model), stdin, AppPaths.WorkingRoot, ct: ct);
        return ClaudeResult.From(r);
    }

    /// <summary>
    /// Vision describe: the CLI has no image flag, so we let it view a local file through the Read
    /// tool. Only Read is enabled, so running under bypassPermissions is safe (it can't edit/run).
    /// </summary>
    /// <summary>Args for a vision describe (shared by the headless and verbose paths).</summary>
    public static List<string> VisionArgs(string prompt, string allowDir, string model = "sonnet")
    {
        var args = new List<string>
        {
            "-p", "--model", model,
            "--safe-mode",              // no CLAUDE.md/skills/hooks/MCP; Read + permissions still work
            "--no-session-persistence",
            "--tools", "Read",
            "--add-dir", allowDir,
            "--permission-mode", "bypassPermissions",
        };
        var memory = DumpMemory();
        if (!string.IsNullOrEmpty(memory))
        {
            args.Add("--append-system-prompt");
            args.Add(memory);
        }
        args.Add(prompt);
        return args;
    }

    public static async Task<ClaudeResult> RunVisionReadAsync(
        string prompt,
        string allowDir,
        string model = "sonnet",
        CancellationToken ct = default)
    {
        var r = await Proc.RunAsync(Executable, VisionArgs(prompt, allowDir, model), null, AppPaths.WorkingRoot, ct: ct);
        return ClaudeResult.From(r);
    }

    /// <summary>
    /// Agentic run with full tools and the given permission mode (used by "Act with"). Runs in
    /// <paramref name="workingDir"/>; output is captured for the after-action summary.
    /// </summary>
    public static async Task<ClaudeResult> RunAgenticAsync(
        string prompt,
        string workingDir,
        string permissionMode = "bypassPermissions",
        string model = "sonnet",
        CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "-p", "--model", model,
            "--no-session-persistence",
            "--permission-mode", permissionMode,
            "--add-dir", AppPaths.ScratchpadDir, // where large clipboard payloads are staged
            prompt,
        };
        var r = await Proc.RunAsync(Executable, args, null, workingDir, ct: ct);
        return ClaudeResult.From(r);
    }
}

public sealed record ClaudeResult(bool Success, string Output, string Error, int ExitCode)
{
    public static ClaudeResult From(ProcResult r) =>
        new(r.Ok, r.StdOut.Trim(), r.StdErr.Trim(), r.ExitCode);

    /// <summary>A human-readable failure message combining stderr and stdout.</summary>
    public string FailureMessage =>
        string.IsNullOrWhiteSpace(Error)
            ? (string.IsNullOrWhiteSpace(Output) ? $"claude exited with code {ExitCode}." : Output)
            : Error;
}
