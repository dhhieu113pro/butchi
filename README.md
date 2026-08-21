# Rust Rewrite

A Windows-first Tauri utility for acting on selected text from a small cursor-adjacent popover.

The current milestone includes:

- A tray-only background app (no centered main window)
- Automatic popover after mouse-drag or double-click text selection
- `Ctrl+Alt+G` as a fallback for keyboard selection
- Windows UI Automation capture for normal browser/editor selections (no clipboard mutation)
- Guarded clipboard fallback for the manual shortcut when only restorable plain text/image data is present
- Translate and Rewrite interaction previews

Translation providers and local LLM inference are not connected yet.

## Development

```sh
npm install
npm run tauri dev
```

After the tray icon appears, select text in a browser or editor by dragging the mouse or double-clicking a word. The action popover should appear beside the selection. It closes five seconds after the pointer leaves it. For keyboard-created selections, press `Ctrl+Alt+G`, then release all three keys.

## Build

```sh
npm run tauri build
```
