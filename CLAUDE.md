# Clipboard Wizard

A Windows-only (WPF, .NET) clipboard power-tool. When the clipboard changes, a small command
menu pops up at the mouse cursor listing every action available for the current clipboard content.

## Build & run

```
dotnet build
dotnet run
```

Requires the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`). There is no main window — the app
lives in the system tray (right-click → Exit). Copy text/an image and the popup appears at the cursor.

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
  `SetWindowPos` + per-monitor DPI (robust across multi-monitor); filter box; keyboard nav
  (↑/↓ select, Enter run, Esc close); single-click an item to run it; closes on focus loss.

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
| Split GIF into PNGs | Image | ⬜ stub (local) |
| Join PNGs into GIF | Image | ⬜ stub (local) |
| .jpg to .png | Image | ⬜ stub (local) |
| Transcribe (AI) — recreate text from image | Image | ⬜ stub (cheapest vision model) |
| Describe — short (5 words) / medium (1 sentence) / long (3 sentences) | Image | ⬜ stub (cheapest vision model) |
| Execute on Tailscale peer | Actions | ⬜ stub |
| Execute on all computers | Actions | ⬜ stub |
| Log to Obsidian daily journal | Actions | ⬜ stub |
| Clipboard Hawk — hide popup, record stack to a tray icon, flush on click | Actions | ⬜ stub |
| Cycle Clipboard — fragment input, advance silently on each paste | Actions | ⬜ stub |
| Intelligent Reformat — `claude -p` reformat to a spec entered after selecting | Actions | ⬜ stub |
| Auto-format and print | Actions | ⬜ stub (printer not yet available) |

### Notes for implementing the AI commands
- Transcribe/Describe must be **fast and cheap** — use the cheapest current Claude vision model
  (Haiku tier). **Confirm the exact model id/pricing against the Claude API reference before coding;
  do not hardcode from memory.** Describe lengths: short = 5 words, medium = 1 sentence, long = 3.
- Intelligent Reformat shells out to `claude -p` with a user-supplied spec.

## Self-write suppression

Any command that writes the clipboard (scripts, PowerShell, future Cycle/Hawk) must call
`context.SuppressNextClipboardChange()` first, or it will re-trigger the popup in a loop.
