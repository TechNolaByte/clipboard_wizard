# Clipboard Wizard

A Windows clipboard power-tool. Copy something and a small command menu pops up at your mouse
cursor (clamped to the screen) listing every action available for what you just copied.

- **Scripts** — your own Python scripts that transform the clipboard in place (stdin → stdout),
  newest on top. Drop `.py` files in the scripts folder (tray → *Open scripts folder*).
- **Image** — operations shown only when the clipboard holds an image or image files
  (gif↔png, jpg→png, AI transcribe/describe).
- **Actions** — run as PowerShell, and a growing set of verbs (Tailscale/all-computers exec,
  Obsidian logging, Clipboard Hawk, Cycle Clipboard, Intelligent Reformat, auto-print).

See [CLAUDE.md](CLAUDE.md) for architecture and the full command roadmap (what's implemented vs stubbed).

## Requirements

- Windows 10/11
- .NET 8 SDK — `winget install Microsoft.DotNet.SDK.8`
- `python` on PATH (for script commands)

## Run

```
dotnet run
```

The app runs in the system tray (no main window). Right-click the tray icon → **Exit** to quit.

## Keyboard

| Key | Action |
|---|---|
| type | filter commands |
| ↑ / ↓ | move selection |
| Enter | run selected command |
| Esc | dismiss |
