using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ClipboardWizard.Services;

/// <summary>
/// Thin wrapper over the <c>claude</c> CLI. All AI features route through here rather than the
/// HTTP API, so they reuse the user's existing Claude Code credentials.
///
/// "Sped-up" invocation: we pass <c>-p --no-session-persistence --strict-mcp-config</c> and run in
/// an empty working directory. We deliberately do NOT use <c>--bare</c>, because that forces
/// API-key-only auth and would ignore the user's OAuth login. Pure-text transforms additionally
/// disable all tools (<c>--tools ""</c>) for the leanest possible run.
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

    /// <summary>
    /// Fast, tool-less text transform. The content to operate on is piped via stdin; the user's
    /// instruction is the prompt; <paramref name="appendSystemPrompt"/> constrains output shape.
    /// Returns trimmed stdout.
    /// </summary>
    public static async Task<ClaudeResult> RunTextAsync(
        string prompt,
        string? stdin,
        string appendSystemPrompt,
        string model = "sonnet",
        CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "-p", "--model", model,
            "--no-session-persistence", "--strict-mcp-config",
            "--tools", "",
            "--append-system-prompt", appendSystemPrompt,
            prompt,
        };
        var r = await Proc.RunAsync(Executable, args, stdin, AppPaths.NeutralDir, ct);
        return ClaudeResult.From(r);
    }

    /// <summary>
    /// Vision describe: the CLI has no image flag, so we let it view a local file through the Read
    /// tool. Only Read is enabled, so running under bypassPermissions is safe (it can't edit/run).
    /// </summary>
    public static async Task<ClaudeResult> RunVisionReadAsync(
        string prompt,
        string allowDir,
        string model = "sonnet",
        CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "-p", "--model", model,
            "--no-session-persistence", "--strict-mcp-config",
            "--tools", "Read",
            "--add-dir", allowDir,
            "--permission-mode", "bypassPermissions",
            prompt,
        };
        var r = await Proc.RunAsync(Executable, args, null, allowDir, ct);
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
            "--add-dir", AppPaths.WorkDir, // where large clipboard payloads are staged
            prompt,
        };
        var r = await Proc.RunAsync(Executable, args, null, workingDir, ct);
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
