# Rust Rewrite

A Windows-first Tauri utility for acting on selected text from a small cursor-adjacent popover.

The current milestone includes:

- A tray-only background app (no centered main window)
- Automatic popover after mouse-drag or double-click text selection
- `Ctrl+Alt+G` as a fallback for keyboard selection
- Double-Ctrl tap as an alternate capture / manual-input path
- Windows UI Automation capture for normal browser/editor selections (no clipboard mutation)
- Guarded clipboard fallback for the manual shortcut when only restorable plain text/image data is present
- **Translate** and **Rewrite** actions wired through a Tauri command
  - Rewrite: offline light grammar cleanup (demo-quality; LLM provider next)
  - Translate: copies the selection so you can paste into any translator (provider not connected yet)
  - Successful results are copied to the clipboard

## Development

```sh
npm install
npm run tauri dev
```

After the tray icon appears, select text in a browser or editor by dragging the mouse or double-clicking a word. The action popover should appear beside the selection. It closes a few seconds after the pointer leaves it. For keyboard-created selections, press `Ctrl+Alt+G`, then release all three keys. Double-tapping Ctrl also opens the flow (with manual input if no selection is found).

## Build

```sh
npm run tauri build
```

## Next

- Plug a real translation provider (and optional local LLM for rewrite)
- Optional: replace selection in the source app instead of clipboard-only
