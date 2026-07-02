using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClipboardWizard.Services;

/// <summary>Result of running an external process: exit code plus captured stdout/stderr.</summary>
public sealed record ProcResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Minimal async process runner shared by the command implementations. Redirects all three
/// standard streams with UTF-8, never pops a console window, and optionally pipes text to stdin.
/// </summary>
public static class Proc
{
    public static async Task<ProcResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        string? stdin = null,
        string? workingDir = null,
        IReadOnlyDictionary<string, string>? env = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        if (!string.IsNullOrEmpty(workingDir))
            psi.WorkingDirectory = workingDir;
        if (env is not null)
            foreach (var (k, v) in env)
                psi.EnvironmentVariables[k] = v;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        if (stdin is not null)
            await proc.StandardInput.WriteAsync(stdin.AsMemory(), ct);
        proc.StandardInput.Close();

        var outTask = proc.StandardOutput.ReadToEndAsync(ct);
        var errTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return new ProcResult(proc.ExitCode, await outTask, await errTask);
    }
}
