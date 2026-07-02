# Clipboard Wizard — working directory

This folder is the working directory for Clipboard Wizard's AI actions. The app runs the
`claude` CLI here with a task derived from the clipboard, and the "Execute as PowerShell"
command opens a terminal here.

## Memory (keep it in situ)
- Persist durable project notes/memory as markdown files in `./config/`, and read them at
  the start of each task so context carries across runs.
- One fact per file; update an existing note rather than duplicating; delete notes that
  turn out to be wrong.

## Layout
- `./config/`     — durable memory (this is where your notes live).
- `./scratchpad/` — transient working files (staged images, large payloads); safe to clobber.
- `./logs/`       — per-action `.rtf` audit logs written by the app; do not edit.
