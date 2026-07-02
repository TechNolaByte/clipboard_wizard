# Clipboard Wizard

A Windows-only (WPF, .NET) clipboard power-tool. When the clipboard changes, a small command
menu pops up at the mouse cursor listing every action available for the current clipboard content.

## Build & run

```
dotnet build
dotnet run
```

For a headless build-and-launch, **double-click `launch.cmd` in Explorer**. It's a self-contained
batch/PowerShell polyglot (the PowerShell build-and-launch body lives after the batch header, which
`exit /b` skips): rebuilds only when a source file changed, runs hidden, and launches detached. It
must stay **CRLF** (enforced by `.gitattributes`) or `cmd` mis-parses it.

Requires the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`). There is no main window — the app
lives in the system tray (right-click → Exit; it also has a **Verbose** toggle). Copy text/an image
and the popup appears at the cursor. Only one instance runs: launching again overrides the previous
one, or — if it's mid-command — asks whether to wait, override, or cancel (`Services/SingleInstance.cs`).
The app/tray icon is `assets/clipwiz.ico`.

AI commands shell out to the **`claude` CLI** (reusing your Claude Code login — no API key). Python
scripts need `python` on PATH. Image transform/split/join use **ffmpeg** (and optionally ImageMagick),
which are downloaded on first use into a gitignored `library-dump/` folder in the project directory
(kept off PATH so they don't shadow other installs).

## Architecture

- `App.xaml.cs` — composition root. Starts `ClipboardMonitor`, owns the tray icon, and shows the
  `CommandPopup` on each clipboard change.
- `Services/ClipboardMonitor.cs` — message-only window + `AddClipboardFormatListener`. Raises
  `ClipboardChanged`. `SuppressNext()` masks our own writes so self-edits (Cycle/Hawk) don't loop.
- `Models/ClipboardPayload.cs` — immutable snapshot of clipboard contents (text/image/files) with
  retry-on-locked capture. Commands inspect `Has*` flags instead of touching the live clipboard.
- `Models/IClipboardCommand.cs` — the command contract: `Name`, `Category`, `CanExecute(payload)`,
  `ExecuteAsync(payload, context)`. `CommandContext.SuppressNextClipboardChange` must be called
  before any clipboard write.
- `Services/CommandRegistry.cs` — **the one place to register commands.** Returns Scripts → Image →
  Actions, filtered by `CanExecute`.
- `UI/CommandPopup.xaml(.cs)` — borderless topmost menu. Placed at the cursor in pixel space via
  `SetWindowPos` + per-monitor DPI (robust across multi-monitor); filter box; a **preview panel**
  (monospace text for text/files, a thumbnail for images); keyboard nav (↑/↓ select, Enter run,
  Esc close); single-click an item to run it; closes on focus loss.
- `Services/ClaudeCli.cs` — wraps the `claude` CLI for all AI features (text transform, vision
  describe, agentic "Act with"). Uses sped-up flags (`-p --no-session-persistence --strict-mcp-config`)
  that don't break OAuth — deliberately **not** `--bare` (which forces API-key auth). Runs in the
  project `working/` dir so Claude picks up the in-situ memory.
- `Services/Proc.cs` — shared async external-process runner (UTF-8, no console window, optional stdin/env).
- `Services/MediaTools.cs` — resolves/downloads ffmpeg + ImageMagick into `library-dump/` on first use.
- `Services/ImageIO.cs` — clipboard-bitmap ⇄ file ⇄ `BitmapSource` helpers.
- `Services/ActionLog.cs` — writes a per-action `.rtf` audit log (original data, instruction, process
  log, final version; images embedded) into `working/logs/`.
- `Services/AppPaths.cs` — project dir, `library-dump/`, and the `working/` area (config/scratchpad/logs).
- `UI/Prompts.cs` — code-only dark-themed dialogs (text input, confirm, scrollable result). `AskText`
  takes an optional `context` string that shows a read-only preview of what will be sent to the AI.
- `Services/SingleInstance.cs` — named mutex/events; new launch overrides the old one (asks first if busy).
- `Services/AppState.cs` — runtime toggles (Verbose).
- `Services/Terminal.cs` — opens terminals, preferring Tabby (`Tabby.exe run …`); used by "Act with" and verbose.
- `Services/VerboseRunner.cs` — verbose mode: runs a step in a visible terminal (observational; not applied).
- `UI/StatusToast.cs` — small non-activating "…running/processing…" chip shown near the cursor during a command.

Text/script commands accept **unrecognized files by path**: `ClipboardPayload.PrimaryText` returns the
clipboard text, or the file path(s) when there's no text, so scripts/AI can open the file themselves.
Every command execution is wrapped in `SingleInstance.EnterBusy/ExitBusy` and (headless) shows a `StatusToast`.

## Adding a command

Implement `IClipboardCommand` and return it from `CommandRegistry`. Use `Category` to place it in a
group, and `CanExecute` to gate it to the right payload (text/image/files). Call
`context.SuppressNextClipboardChange()` immediately before writing to the clipboard.

## Command roadmap

Categories: **Scripts** (in-situ Python), **Image** (only when clipboard holds an image/image files),
**Actions** (verbs).

| Command | Group | Status |
|---|---|---|
| Python scripts (stdin → stdout, newest first) | Scripts | ✅ implemented (`PythonScriptCommand`) |
| Execute as PowerShell — opens a terminal with the code pre-typed, awaiting Enter | Actions | ✅ implemented (`PowerShellCommand`) |
| Split GIF into PNGs | Image | ✅ implemented (ffmpeg, `SplitGifCommand`) |
| Join PNGs into GIF | Image | ✅ implemented (ffmpeg, `JoinPngsToGifCommand`) |
| .jpg to .png | Image | ✅ implemented (native WPF codecs, `JpgToPngCommand`) |
| Transform image — NL spec → ImageMagick/ffmpeg args | Image | ✅ implemented (`TransformImageCommand`) |
| Describe — title (~5 words) / verbose (~3 sentences) | Image | ✅ implemented (Sonnet vision via CLI, `DescribeImageCommand`) |
| Transcribe (AI) — recreate text from image | Image | ⬜ stub (not yet wired) |
| Reformat in situ — LLM (Sonnet via CLI, spec entered after selecting) | Actions | ✅ implemented (`ReformatLlmCommand`) |
| Reformat in situ — Python script (Sonnet writes + saves a reusable script, then runs it) | Actions | ✅ implemented (`ReformatPythonScriptCommand`) |
| Act with… — opens interactive Claude Code in a Tabby terminal (normal permissions, no stakes dialog) | Actions | ✅ implemented (`ActWithCommand`) |
| Send to peers — runs the clipboard content on the fleet (`fleet.ps1 run`, optional `-Only`) | Actions | ✅ implemented (`SendToPeersCommand`) |
| Log to Obsidian daily journal | Actions | ⬜ stub |
| Clipboard Hawk — hide popup, record stack to a tray icon, flush on click | Actions | ⬜ stub |
| Cycle Clipboard — fragment input, advance silently on each paste | Actions | ⬜ stub |
| Auto-format and print | Actions | ⬜ stub (printer not yet available) |

### AI commands (via the `claude` CLI)
- All AI features route through `Services/ClaudeCli.cs` (the CLI, **not** the HTTP API) so they reuse
  the user's Claude Code login. Model is **Sonnet** (`--model sonnet`, i.e. `claude-sonnet-5`).
- The **lean ops** (text transforms + vision describe) run under `--safe-mode`: it disables CLAUDE.md
  auto-discovery, skills, hooks, and MCP while keeping OAuth auth, built-in tools, and permissions —
  so no project docs leak into a clipboard transform. Since safe-mode also skips the working memory,
  `ClaudeCli.DumpMemory()` reads `working/config/` and injects it via `--append-system-prompt`.
- Text transforms pipe clipboard text to the CLI's stdin and disable tools (`--tools ""`) for speed.
- **Describe** uses vision: the CLI has no image flag, so the image is written to a temp file and
  viewed via the Read tool (`--tools "Read"`, restricted, under `bypassPermissions`).
- **Act with** runs agentically with full tools under `--permission-mode bypassPermissions`, gated by
  a stakes-confirmation dialog, and is asked to end with a "## Changes made" summary shown afterward.

### Bundled media binaries
- `Services/MediaTools.cs` downloads ffmpeg on first use into `library-dump/` (gitignored, off PATH),
  invoked by absolute path. ImageMagick is best-effort (library-dump/PATH copy if present); its
  portable-zip URL isn't machine-resolvable, so image **Transform** falls back to ffmpeg when magick
  is absent. Drop a `magick.exe` into `library-dump/magick/` to enable it.

### Working directory & audit logs
- `working/` (inside the project) is the working area for all AI/terminal work. Both the `claude` CLI
  and the "Execute as PowerShell" terminal cd into it, so Claude discovers `working/CLAUDE.md`, which
  points it at `working/config/` for durable, in-situ project memory.
  - `working/config/` — Claude's memory (tracked in git).
  - `working/scratchpad/` — transient staging for images / large payloads (**gitignored**).
  - `working/logs/` — one `.rtf` per action, named `yyyy-MM-ddTHH-mm-ss.rtf`, containing the original
    clipboard data, the instruction, the process log, and the final version (**gitignored** — the logs
    hold raw clipboard contents, which may be sensitive). `Services/ActionLog.cs` writes them; every
    command that acts on clipboard data logs.
- This editing guide lives in `claude-instructions-for-editing-project.md`, **not** `CLAUDE.md`, so it
  is not auto-loaded into the clipboard app's `claude` runs (which cd into `working/` and would otherwise
  inherit a repo-root `CLAUDE.md`). The root `CLAUDE.md` is a one-line pointer to this file: editing
  sessions follow it and read this guide; clipboard commands only ever see that one line (and the lean
  text runs pass `--tools ""`, so they can't open files anyway).

## Self-write suppression

Any command that writes the clipboard (scripts, PowerShell, future Cycle/Hawk) must call
`context.SuppressNextClipboardChange()` first, or it will re-trigger the popup in a loop.
