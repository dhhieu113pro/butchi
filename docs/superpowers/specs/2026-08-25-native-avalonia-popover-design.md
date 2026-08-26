# Task 8 — Native Avalonia Popover Design

## Goal
Build a reusable, persistent native Avalonia popover for Butchi that presents Translate/Rewrite streaming state efficiently without recreating a webview/window per action.

## Architecture
- Keep one persistent `PopoverWindow` instance. Show/hide and reposition it rather than recreating it.
- Keep UI behavior in `Butchi.App`; Core remains free of Avalonia dependencies.
- `PopoverViewModel` owns independent Translate and Rewrite presentation state keyed by scheduler run IDs. Updates for stale run IDs are rejected.
- Streamed output is accumulated and UI notifications are batched instead of forcing layout for every inference fragment.
- Window sizing and placement are pure policies so they can be tested without a visible desktop.

## Window behavior
- Borderless, topmost, hidden from taskbar.
- Reusable instance with Escape-to-hide.
- Auto-grow content up to a bounded maximum height, then scroll.
- Expose cursor-adjacent placement input and clamp to supplied working-area bounds.
- Actual Win32 cursor/selection acquisition and hooks are explicitly deferred to Task 9.

## Interaction
- Translate and Rewrite have independent run/output/busy state.
- Favorite-language actions request a new Translate run for the chosen language.
- Manual-input mode is represented distinctly from selection-origin input.
- Auto-hide is configurable and represented as view-model state/timing behavior.
- Theme supports system/light/dark through Avalonia requested theme variants.

## Testing
Task 8 tests run headlessly and cover:
- stale run update rejection
- Translate/Rewrite state isolation
- batched streaming notification behavior
- favorite-language translate rerun request
- auto-hide state/timing policy
- bounded window sizing
- cursor-adjacent placement and working-area clamping

## Non-goals
- Global hotkeys
- Win32 text selection acquisition
- Cursor monitoring/hooks
- Clipboard/selection replacement integration
- Tray lifecycle

Those platform integrations belong to later isolated tasks.