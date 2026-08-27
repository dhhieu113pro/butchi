# Task 10 — Management UI Design

> Startup and first-run routing in this historical design are superseded by [2026-08-27-startup-welcome-setup-design.md](2026-08-27-startup-welcome-setup-design.md), which is authoritative for current startup behavior.

## Goal
Add a native Avalonia management shell for Settings, History, Models, and Status/About while preserving Butchi’s existing persistent stores, inference lifetime rules, and lightweight popover behavior.

## Chosen approach
Use one reusable `ManagementWindow` with left navigation and page content rather than separate top-level windows or a single dense settings form.

Primary sections:
- Settings
- History
- Models
- Status / About

The window is not part of the fast selection/popover path and is opened explicitly from tray or application commands.

## Architecture

### App composition
`Butchi.App` owns the root service provider and composition. Existing Core, Inference, Infrastructure, and Windows platform services are registered once and shared across all windows.

`IInferenceEngine` must be registered as a singleton. The same engine instance is used by the popover, model/status views, and settings reload coordination. No window or page may construct its own inference engine.

The app root creates reusable top-level windows (`PopoverWindow`, `ManagementWindow`) from shared services and controls their lifecycle.

### Management shell
`ManagementWindow` contains a compact left navigation rail and one active page host. Navigation is state-driven and does not create duplicate top-level windows.

Suggested page order:
1. Settings
2. History
3. Models
4. Status / About

The shell remembers the last selected management page during the current process lifetime. First-run routing may override it and open Models.

## Settings

### Editing model
Settings use a working copy of `AppConfig`. Edits do not write to disk on every keystroke.

Actions:
- Save: validate and persist atomically through `JsonConfigStore`.
- Reset unsaved: reload the last persisted config into the working copy.
- Restore defaults: copy `AppConfig.Default` values into the working copy; user must still press Save.

### Reload policy
Each setting is classified as either live-applied or reload-required.

Live-applied examples:
- theme
- favorite languages
- auto-hide / UI interaction preferences
- result action preferences where no inference-engine rebuild is needed

Reload-required examples:
- model path/model selection
- backend preference
- context or model-load options that affect the loaded inference session

After Save, if a reload-required value changed, Settings exposes a clear `Reload model` state/action. Saving configuration must not silently create a second engine or rebuild the engine from a page constructor.

A successful reload updates the singleton engine’s status. A reload failure leaves the saved configuration intact and exposes the error/status without crashing the management window.

## History

History page is a presentation/orchestration layer over `SqliteHistoryStore`.

Capabilities:
- recent list
- text search
- action/type filter where supported by the existing store
- bounded result count
- delete one entry
- clear all history with explicit confirmation in UI
- copy input/result from an entry

The page must not duplicate database queries in App code if an equivalent store API already exists. Any missing store behavior is added behind an interface and TDD-tested outside the Avalonia view.

History content is not logged by default.

## Models

Model page orchestrates existing catalog/download/model-management services.

Capabilities:
- show available catalog entries
- show local/downloaded state
- download with progress and cancellation
- show separate download and load state
- load/switch model through the shared inference-engine/model-lifetime service
- unload-before-delete
- delete local model
- open/recreate model directory where existing infrastructure supports it

The UI clearly separates these states:
- not downloaded
- downloading
- downloaded, unloaded
- loading
- loaded
- error

Backend and device shown for the loaded model come from actual inference status, not desired configuration alone.

## First-run flow

On application startup, determine whether a usable configured/local model exists.

If no usable model exists:
- keep the fast popover unavailable for inference actions
- open `ManagementWindow` directly on Models
- show a concise first-run explanation and recommended/default model
- let the user download and load a model

First-run is considered complete when a usable model is loaded or a valid existing local model is selected and loaded. No separate one-time wizard state is required unless later UX work proves necessary.

## Theme and controls

Theme choices are System, Light, and Dark and apply to all Avalonia windows from one application-level theme service/state.

Settings expose the existing configuration concepts rather than inventing parallel config:
- Translate/Rewrite enablement
- target/default language
- favorite languages
- prompt/profile controls already represented in Core config
- result action behavior
- backend/model controls
- relevant inference parameters already supported by `AppConfig`

Controls bind to typed properties/enums, not raw strings where an enum/domain type exists.

## Tray integration

Task 10’s tray surface provides commands for:
- Open / show Butchi management window
- Settings
- History
- Models
- toggle selection trigger behavior if the underlying trigger service exposes this safely
- Exit

Tray commands reuse the single `ManagementWindow` and select the requested page rather than creating separate windows.

If the existing Windows interaction layer still lacks concrete tray/single-instance runtime implementation, Task 10 may add the tray UI/lifecycle needed by the management experience, but should not redesign selection/hotkey behavior already defined by Task 9.

## Status / About

Display runtime facts useful to the user:
- application version
- loaded model
- actual backend
- actual device where available
- inference load state
- model directory

Status must distinguish desired backend from actual backend if they differ because of fallback.

## Error handling

Expected operational errors (config write, model load/download, history operation) are surfaced as page-level status/errors and do not terminate the app.

Selected text, prompts, history bodies, and generated text are not written to logs by default.

## Testing strategy

Production behavior remains TDD-first. Headless tests cover ViewModels/policies and DI composition; visual Avalonia construction is kept thin.

Required coverage includes:
- root DI resolves exactly one `IInferenceEngine` instance across consumers
- management navigation selects pages without duplicate window semantics
- settings working-copy save/reset/default behavior
- live vs reload-required change detection
- reload action uses the existing singleton engine/lifetime service
- history search/delete/clear orchestration
- first-run routes to Models when no usable model exists
- model state mapping and download/load command state
- theme changes propagate through application-level theme state
- tray command routing chooses the requested management page
- backend/device display uses actual inference status

Full solution tests and win-x64/win-arm64 publish-smoke checks remain required.

## Implementation slicing
Task 10 stays one product task but may be delivered through multiple independently green PRs:

1. DI composition + management shell/navigation + settings save/reload policy
2. History page
3. Models + first-run flow + actual status display
4. Tray routing + final Task 10 integration

Each PR must be reviewable and green before merge. No Task 11 packaging/screenshot work is included.

## Non-goals
- packaging/MSIX/Store changes (Task 11)
- diagnostics/screenshot CI redesign (Task 11)
- performance/parity benchmark gate (Task 12)
- replacing existing persistence formats
- creating multiple inference-engine instances
