using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ClipboardWizard.Services;

/// <summary>
/// Fork-from-seed shots — Tadpole's routing-dispatch pattern (tabbitha-cloud
/// <c>internal/tadpole/seed.go</c>), ported for the clipboard commands.
///
/// A cold <c>claude --print</c> with a fixed instruction caches nothing across calls, so every
/// title / transcription / reformat re-pays its whole prompt. Instead each command keeps ONE
/// durable seed session whose system prompt is that command's standing instruction (plus the
/// working/config memory). Every real call is a fork of the seed — <c>--resume &lt;seed&gt;
/// --fork-session --no-session-persistence</c> — so the prefix comes out of the prompt cache and
/// only the per-shot tail (the image path, the text) is new: cheaper and faster, and every fork
/// starts pristine ("rewound" to the seed), so one shot can never contaminate the next. That is
/// what makes a 40-file rename cheap enough to run in parallel.
///
/// The seed is re-cut only when its hash (instruction + model + tool shape) moves or its
/// transcript is gone; a fork that fails re-seeds once and retries. Seeds live under
/// <c>working/seeds/</c> (their own Claude Code transcript slug); forks persist nothing.
/// </summary>
public static class Seeds
{
    private const string ReadyPrompt =
        "The system prompt above is your context for this session. Do not act on it yet. Reply exactly: READY";

    /// <summary>A fork fired within ~1 s of its seed reads only half the cache; give the write a beat.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    /// <summary>How many forks of one seed run at once during bulk work.</summary>
    public const int BulkParallelism = 3;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly Regex Unsafe = new(@"[^A-Za-z0-9._-]", RegexOptions.Compiled);
    private static readonly Regex SlugRe = new(@"[^A-Za-z0-9]", RegexOptions.Compiled);

    private sealed class SeedState
    {
        [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
        [JsonPropertyName("hash")] public string Hash { get; set; } = "";
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("created")] public string Created { get; set; } = "";
        [JsonPropertyName("chars")] public int Chars { get; set; }
        [JsonPropertyName("cost_usd")] public double CostUsd { get; set; }
        [JsonPropertyName("last_shot")] public string? LastShot { get; set; }
    }

    private sealed class CliJson
    {
        [JsonPropertyName("result")] public string? Result { get; set; }
        [JsonPropertyName("session_id")] public string? SessionId { get; set; }
        [JsonPropertyName("total_cost_usd")] public double? TotalCost { get; set; }
        [JsonPropertyName("is_error")] public bool IsError { get; set; }
        [JsonPropertyName("subtype")] public string? Subtype { get; set; }
        [JsonPropertyName("usage")] public CliUsage? Usage { get; set; }
    }

    private sealed class CliUsage
    {
        [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
        [JsonPropertyName("cache_read_input_tokens")] public int CacheRead { get; set; }
        [JsonPropertyName("cache_creation_input_tokens")] public int CacheWrite { get; set; }
    }

    private static string SeedDir => AppPaths.SeedsDir;
    private static string Safe(string name) => Unsafe.Replace(name, "-");
    private static string SlowPath(string name) => Path.Combine(SeedDir, Safe(name) + "-slow.md");
    private static string StatePath(string name) => Path.Combine(SeedDir, Safe(name) + ".json");

    /// <summary>Claude Code keeps the seed session under ~/.claude/projects/&lt;slug of the cwd&gt;/.</summary>
    private static string? TranscriptPath(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var slug = SlugRe.Replace(Path.GetFullPath(SeedDir), "-");
        return Path.Combine(home, ".claude", "projects", slug, sessionId + ".jsonl");
    }

    private static string Hash(SeedSpec spec)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{spec.Slow}\0{spec.Model}|{(spec.Vision ? "vision" : "text")}"));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    /// <summary>argv for the seed (resume == null) or a fork of it. Same tool shape both ways, so
    /// the cached prefix (system prompt + tool definitions) is identical.</summary>
    private static List<string> Args(SeedSpec spec, string? resume)
    {
        var a = new List<string>
        {
            "--print", "--safe-mode", "--output-format", "json",
            "--system-prompt-file", SlowPath(spec.Name),
            "--model", spec.Model,
        };
        if (spec.Vision)
            a.AddRange(new[] { "--tools", "Read", "--add-dir", AppPaths.ScratchpadDir, "--permission-mode", "bypassPermissions", "--max-turns", "4" });
        else
            a.AddRange(new[] { "--tools", "", "--max-turns", "1" });
        if (resume is not null)
            a.AddRange(new[] { "--resume", resume, "--fork-session", "--no-session-persistence" });
        return a;
    }

    private static SeedState? ReadState(string name)
    {
        try { return JsonSerializer.Deserialize<SeedState>(File.ReadAllText(StatePath(name))); }
        catch { return null; }
    }

    private static void WriteState(string name, SeedState st)
    {
        var tmp = StatePath(name) + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(st, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, StatePath(name), overwrite: true);
    }

    private static async Task<(CliJson? json, ProcResult raw)> RunAsync(IEnumerable<string> args, string prompt, CancellationToken ct)
    {
        var r = await Proc.RunAsync(ClaudeCli.Executable, args, prompt, SeedDir, ct: ct);
        if (!r.Ok) return (null, r);
        try { return (JsonSerializer.Deserialize<CliJson>(r.StdOut), r); }
        catch { return (null, r); }
    }

    private static string Failure(CliJson? j, ProcResult r) =>
        j is null
            ? (string.IsNullOrWhiteSpace(r.StdErr) ? (string.IsNullOrWhiteSpace(r.StdOut) ? $"claude exited with code {r.ExitCode}." : Tail(r.StdOut)) : Tail(r.StdErr))
            : $"claude reported {j.Subtype}: {Tail(j.Result ?? "")}";

    private static string Tail(string s) => s.Length <= 600 ? s.Trim() : s[^600..].Trim();

    /// <summary>A live seed for the spec: reused while its hash and transcript hold, re-cut otherwise.</summary>
    private static async Task<(SeedState st, bool seeded)> EnsureSeedAsync(SeedSpec spec, bool force, CancellationToken ct)
    {
        var want = Hash(spec);
        var old = ReadState(spec.Name);
        if (old is not null && !force && old.Hash == want && TranscriptPath(old.SessionId) is { } tp && File.Exists(tp))
            return (old, false);

        Directory.CreateDirectory(SeedDir);
        File.WriteAllText(SlowPath(spec.Name), spec.Slow);
        var (j, r) = await RunAsync(Args(spec, null), ReadyPrompt, ct);
        if (j is null || j.IsError || (!string.IsNullOrEmpty(j.Subtype) && j.Subtype != "success"))
            throw new InvalidOperationException("seed: " + Failure(j, r));
        if (string.IsNullOrEmpty(j.SessionId))
            throw new InvalidOperationException("seed: claude returned no session id");

        var st = new SeedState
        {
            SessionId = j.SessionId, Hash = want, Model = spec.Model,
            Created = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), Chars = spec.Slow.Length, CostUsd = j.TotalCost ?? 0,
        };
        WriteState(spec.Name, st);
        if (old is not null && old.SessionId != st.SessionId && TranscriptPath(old.SessionId) is { } op)
            try { File.Delete(op); } catch { /* best effort */ }
        await Task.Delay(Settle, ct);
        return (st, true);
    }

    /// <summary>
    /// One fork-from-seed shot: the seed is (re)made on demand under the seed's lock, the fork runs
    /// outside it so bulk callers can overlap forks. A failed fork re-seeds once and retries.
    /// </summary>
    public static async Task<ClaudeResult> ShotAsync(SeedSpec spec, string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(spec.Name)) throw new ArgumentException("seed name required", nameof(spec));
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("empty prompt", nameof(prompt));

        var sw = Stopwatch.StartNew();
        var gate = Locks.GetOrAdd(spec.Name, _ => new SemaphoreSlim(1, 1));
        string lastError = "";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            SeedState st; bool seeded;
            await gate.WaitAsync(ct);
            try { (st, seeded) = await EnsureSeedAsync(spec, force: attempt > 0, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { lastError = ex.Message; continue; }
            finally { gate.Release(); }

            var (j, r) = await RunAsync(Args(spec, st.SessionId), prompt, ct);
            if (j is null || j.IsError || (!string.IsNullOrEmpty(j.Subtype) && j.Subtype != "success"))
            {
                lastError = Failure(j, r);
                if (IsUsageExhausted(lastError)) break; // re-seeding cannot buy back an allowance
                continue;
            }

            st.LastShot = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            try { WriteState(spec.Name, st); } catch { /* the seed still works without the stamp */ }
            var u = j.Usage;
            var usage = $"seed {spec.Name} ({spec.Model}{(seeded ? ", freshly seeded $" + st.CostUsd.ToString("F4") : "")}) · " +
                        $"cache_read={u?.CacheRead ?? 0} cache_write={u?.CacheWrite ?? 0} in={u?.InputTokens ?? 0} out={u?.OutputTokens ?? 0}" +
                        $"{(j.TotalCost is { } c ? $" · ${c:F4}" : "")} · {sw.Elapsed.TotalSeconds:F1}s";
            return new ClaudeResult(true, (j.Result ?? "").Trim(), "", 0) { Usage = usage };
        }
        return new ClaudeResult(false, "", lastError, 1) { Usage = $"seed {spec.Name}: failed after re-seed · {sw.Elapsed.TotalSeconds:F1}s" };
    }

    private static bool IsUsageExhausted(string err) =>
        err.Contains("usage limit", StringComparison.OrdinalIgnoreCase) ||
        err.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
        err.Contains("out of extra usage", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Run many shots of one seed with bounded parallelism. <paramref name="onDone"/> is called on
    /// the caller's context after each shot with the running count, for progress toasts.
    /// </summary>
    public static async Task<ClaudeResult[]> BulkAsync(SeedSpec spec, IReadOnlyList<string> prompts, Action<int>? onDone = null, CancellationToken ct = default)
    {
        var results = new ClaudeResult[prompts.Count];
        if (prompts.Count == 0) return results;
        // Cut the seed once, up front, so the forks never race the seed lock.
        var gate = Locks.GetOrAdd(spec.Name, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { await EnsureSeedAsync(spec, force: false, ct); }
        catch (OperationCanceledException) { throw; }
        catch { /* ShotAsync will re-seed and report per prompt */ }
        finally { gate.Release(); }

        var done = 0;
        using var slots = new SemaphoreSlim(BulkParallelism, BulkParallelism);
        var tasks = new List<Task>();
        for (var i = 0; i < prompts.Count; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(async () =>
            {
                await slots.WaitAsync(ct);
                try
                {
                    try { results[idx] = await ShotAsync(spec, prompts[idx], ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { results[idx] = new ClaudeResult(false, "", ex.Message, 1); }
                }
                finally { slots.Release(); }
                var n = Interlocked.Increment(ref done);
                onDone?.Invoke(n);
            }, ct));
        }
        await Task.WhenAll(tasks);
        return results;
    }
}

/// <summary>One seed: a namespace, its standing instruction (the slow layer), and the tool shape.</summary>
public sealed record SeedSpec(string Name, string Slow, bool Vision = false, string Model = "sonnet");

/// <summary>The commands' standing instructions — each is the whole system prompt of its seed;
/// the per-shot tail names the image file or carries the text.</summary>
public static class SeedPrompts
{
    private static string Mem(string slow) => ClaudeCli.WithMemory(slow);

    public static SeedSpec DescribeTitle => new("describe-title", Mem(
        "You title images so the title can be used as a file name. Each request names one image file " +
        "path. View that file with the Read tool, then reply with a concise title of about 5 words: " +
        "plain words, no quotes, no punctuation, no file extension. Output only the title, nothing else."), Vision: true);

    public static SeedSpec DescribeVerbose => new("describe-verbose", Mem(
        "You describe images. Each request names one image file path. View that file with the Read " +
        "tool, then describe it in about 3 sentences. Output only the description, nothing else."), Vision: true);

    public static SeedSpec Transcribe => new("transcribe", Mem(
        "You transcribe the text inside images. Each request names one image file path. View that " +
        "file with the Read tool, then output the exact text it contains, verbatim, preserving line " +
        "breaks and reading order. Output only the transcribed text and nothing else. If the image " +
        "contains no text, output nothing."), Vision: true);

    public static string ImageTail(string imagePath) => $"Image file: {imagePath}";

    public static SeedSpec ReformatLlm => new("reformat-llm", Mem(
        "You transform text. Each request has an INSTRUCTION section and a TEXT section. Apply the " +
        "instruction to the text and output ONLY the resulting text — no explanations, no preamble, " +
        "and no surrounding markdown code fences."));

    public static string ReformatTail(string instruction, string text) => $"INSTRUCTION:\n{instruction}\n\nTEXT:\n{text}";

    public static SeedSpec ReformatPython(string systemPrompt) => new("reformat-python", Mem(systemPrompt));

    public static SeedSpec TransformImage(string toolName, string systemPrompt) => new($"transform-image-{toolName}", Mem(systemPrompt));
}
