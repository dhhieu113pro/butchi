# Butchi Settings UI/UX Redesign Design

## Goal
Restructure Butchi Settings from a long card stack into a compact desktop settings experience that is easier to scan, keeps common controls prominent, and progressively discloses advanced local-LLM controls.

## Design direction
Keep the existing Hallmark visual language: Cobalt tokens, light/dark/system themes, 4pt spacing, restrained motion, compact technical styling, and the current popover visual direction. This change focuses on information architecture rather than decorative redesign.

## Navigation
Use five destinations:

1. **General** — appearance, Translate/Rewrite enablement, target/favorite languages, and result behavior.
2. **Prompts** — Translate/Rewrite segmented tabs, prompt profile, and editable system prompt.
3. **Model** — model picker, download/load/status, device preference, available backend summary, with inference tuning under an Advanced disclosure.
4. **History** — search, action filter, retention, history entries, copy/delete actions, and clear history.
5. **About & Privacy** — local-processing explanation, network/model-download explanation, destructive local-data cleanup, version, license, and project/support information.

On normal desktop widths, use a left navigation rail and a content pane. On narrow widths, navigation becomes a horizontally scrollable top tab row. Navigation changes the visible section without opening separate windows.

## Auto-save
Settings auto-save after user changes instead of requiring a bottom Save button. Show a small non-blocking status such as `Saving…`, `Saved`, or an error. Do not show repetitive success toasts. Destructive actions remain explicit buttons and require their existing confirmation behavior.

## General
Put the controls most users need here: Theme, Enable Translate, Enable Rewrite, target language, favorite target languages, and after-action behavior. Preserve existing values and persistence semantics.

## Prompts
Combine the current Translation prompt and Rewrite prompt cards into one destination. A Translate/Rewrite segmented control selects which prompt is being edited. Each mode retains its existing profile choices and editable system prompt. Editing preset text switches the selected profile to Custom exactly as today.

## Model
When no model is installed/available, model setup is the dominant content: recommended model, download status, Download, and Load model. When ready, show a compact loaded/status summary.

Keep Device preference visible. Put Max tokens, Temperature, Maximum GPU layers, backend pills/diagnostics, and detailed available-device information inside an `Advanced` disclosure so normal users are not confronted with inference tuning.

## History
History is first-class content rather than a settings card. Keep search and action filtering near the top. Keep retention available on the same destination but visually secondary. Keep Refresh/Clear controls and the existing local-only explanation. History entries retain their existing actions and behavior.

## About & Privacy
Explain that selected text/history remain local and that network access is used for explicit model downloads. Keep the destructive `Delete history + downloaded models` action visually separated from informational content. Show version, MIT license, and project/support information below.

## Interaction and accessibility
All buttons, tabs, inputs, selects, textareas, disclosures, and navigation items need explicit `:focus-visible` treatment using the existing focus token. Active navigation must be identifiable without relying on color alone. Preserve reduced-motion behavior. Avoid horizontal overflow at narrow widths. Use semantic buttons for navigation/tabs and appropriate ARIA state (`aria-selected`, `aria-controls`, `aria-expanded`) where applicable.

## Popover scope
Do not redesign the popover. Make only two small UX refinements if tests confirm behavior: remove redundant `Done` success text when a visible result already communicates success, and label post-success action buttons as rerun actions only if that does not make the compact layout noisier. Loading and error states remain explicit.

## Testing
Update/add DOM-oriented tests for navigation, responsive class/state behavior where practical, auto-save, prompt mode switching, advanced disclosure, model setup/ready states, History isolation, and keyboard-accessible ARIA states. Preserve existing settings persistence and backend/model tests. Run frontend tests/build plus Rust checks required by the repository CI before completion.
