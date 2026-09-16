# Clipboard Wizard

A Windows clipboard power-tool. Copying an **image** pops the menu instantly (without stealing focus);
for text/files a single
copy is silent — **copy the same thing twice** (a second Ctrl+C on the same selection) and a small
command menu pops up at your mouse cursor (clamped to the screen) listing every action available for
what you copied.

![The command popup with a text payload](assets/screenshot-text.png)

When the clipboard holds an image, image operations appear too:

![The command popup with an image payload](assets/screenshot-image.png)

- **Research** — ask an AI about the clipboard: *Ask Claude* (opens interactive Claude Code with a
  "tell me about this" prompt — the default selection, so Enter runs it) and *Search online*
  (Google / Perplexity, incl. reverse-image search).
- **Scripts** — your own Python scripts that transform the clipboard in place (stdin → stdout),
  newest on top. Drop `.py` files in the scripts folder (tray → *Open scripts folder*), or let
  *Reformat in situ — Python script* write one for you. Each has a ✕ to delete it.
- **Image** — operations shown only when the clipboard holds an image or image files:
  transcribe (exact text / OCR), transform (ffmpeg/ImageMagick), gif↔png, jpg→png, AI describe
  (title / verbose), and *Save file with intelligent name* (drops the image into Downloads as
  `<yyyy-MM-dd HH-mm-ss> <AI title>.png`).
- **Explorer** — right-click any image files → *Rename with intelligent name*: each is renamed in
  place to `<file's timestamp> <AI title>.<ext>` (install once with `install-context-menu.ps1`).
- **Actions** — Execute as PowerShell, Reformat in situ (LLM or a saved Python script),
  Act with… (interactive Claude Code), Send to peers (fleet), and more.
- **Collect** — capture modes: Log to Obsidian, Clipboard Hawk (record a stack), Clipboard Cycle
  (fragment + paste), Auto-format and print.

AI features shell out to the **`claude` CLI** (reusing your Claude Code login — no API key needed).

See [the editing guide](claude-instructions-for-editing-project.md) for architecture and the full
command roadmap (what's implemented vs stubbed).

## Requirements

- Windows 10/11
- .NET 8 SDK — `winget install Microsoft.DotNet.SDK.8`
- `python` on PATH (for script commands)
- The `claude` CLI (for the AI commands); optionally Tabby (for *Act with…*) and the fleet (for *Send to peers*)

## Run

```
dotnet run
```

**Or just double-click `launch.cmd`.** It's a self-contained launcher that rebuilds only when a
source file changed, runs hidden, and starts the app detached — so there's no console window left
behind. Pin it or drop a shortcut on your desktop for one-click startup.

The app runs in the system tray (no main window). Right-click the tray icon for a **Verbose** toggle
(runs script/LLM commands in a visible terminal) and **Exit**. Only one instance runs at a time —
launching again takes over the previous one (and asks first if it's mid-command).

To summon the popup for the current clipboard, just **copy the same content again** — a re-copy of
identical content is the trigger, so a normal one-off copy never interrupts you.

**Image copies never interrupt you either.** The popup that an image copy summons stays on top but
takes no focus: keep typing where you were and it quietly closes, or click it to use it. Click it and
it's yours — filter, arrow around, Enter to run — until Esc or a click away.

**Screen captures also get their file put on the clipboard.** Windows saves Win+Shift+S / PrintScreen
shots into `Pictures\Screenshots` but only puts the pixels on the clipboard, so there's nothing to
paste into a terminal. Clipboard Wizard spots the saved shot and adds it: paste into a terminal or an
AI prompt and you get the **path**, paste into Explorer or an upload box and you get the **file**, paste
into an editor or chat app and you still get the **image**.

## Keyboard

| Key | Action |
|---|---|
| type | filter commands |
| ↑ / ↓ | move selection |
| Enter | run selected command |
| Esc | dismiss |

An image-triggered popup has no focus, so **any keypress** (or a mouse press anywhere but on it)
dismisses it — until you click it, after which the table above applies.
