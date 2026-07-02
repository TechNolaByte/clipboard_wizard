# Prior art — earlier prototypes

Before this .NET/WPF version, the project was prototyped three other ways (all since deleted). This
note preserves the ideas worth keeping.

## The prototypes

- **`clipboard_hawk` (Rust)** — a tiny `clipboard-rs` watcher that printed every clipboard change to
  stdout. Essentially the seed of the "Clipboard Hawk" idea (watch + record the clipboard stack).
- **`clipboard_wizard` (Python + a Rust twin)** — a **global-hotkey script launcher**: press **F15**,
  a small always-on-top menu of `scripts/*.py` appears, pick one by number/letter. Included reusable
  scripts and a `hit.wav` capture sound. The Rust twin used `RegisterHotKey` + `egui` for the same UX.
- **`ClipboardWizard` (AutoHotkey)** — `Ctrl+Win+C` grabs the selection (`^c` + `ClipWait`), shows a
  ListBox of `scripts/*` with a read-only preview pane, and runs the picked script.

## Ideas worth adopting (not yet in the current app)

- **Global-hotkey activation** as an alternative to (or alongside) the clipboard-change trigger — F15
  or `Ctrl+Win+C` to summon the menu on demand. The current app only pops up on clipboard change.
- **Per-item shortcut keys** — prefix each command with `1`-`9`, `0`, then `a`-`z` for instant launch,
  instead of only ↑/↓ + filter. (The old spec's selective-key-suppression state machine is the
  fiddly part; our popup already owns keyboard focus, so it's simpler here.)
- **A read-only preview pane** — already adopted (the popup shows a clipboard preview).

## Reusable pieces carried over

- **`convert_path_slashes.py`** and **`search_clipboard_lines.py`** — adapted to the stdin→stdout
  contract and added to the seeded scripts (`Services/CommandRegistry.cs`).
- **`assets/hit.wav`** — capture sound, kept for a future **Clipboard Hawk** implementation.

## Cycle Clipboard — reference algorithm

The stubbed *Cycle Clipboard* command comes straight from the old `clipboard_cycle_paste_each.py`:

1. Split the current clipboard text into lines → a queue of fragments.
2. Put fragment 0 on the clipboard.
3. On each paste (the old version hooked `Ctrl+V` globally), advance: put the next fragment on the
   clipboard, silently, until exhausted.

In this app, "advance on paste" is the hard part (a global paste hook). A simpler first cut: advance
on a hotkey or a tray action, using `ClipboardMonitor.SuppressNext()` before each write so cycling
doesn't re-trigger the popup.
