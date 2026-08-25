# Butchi Avalonia + LLamaSharp Migration Design

Date: 2026-08-25
Status: Proposed design approved in chat; implementation not started
Branch: `design/avalonia-llamasharp-migration`

## 1. Summary

Butchi will migrate from the current Tauri + TypeScript + Rust + `rs-llama` architecture to a Windows-first .NET 10 application built with Avalonia and LLamaSharp.

The migration replaces the WebView/Tauri IPC boundary and the Rust application layer with one .NET process. Avalonia owns the popover, settings, tray, and history UI. Windows-specific capture and replacement behavior lives behind platform interfaces. LLamaSharp owns local llama.cpp inference, model loading, streaming, and backend selection.

The migration is driven by responsiveness and maintainability. It is not considered successful merely because the new application builds or because the language/runtime changed. It must preserve Butchi's behavior and meet explicit performance gates against the current Tauri implementation.

## 2. Goals

1. Improve perceived responsiveness of the Butchi popover and streaming UI.
2. Remove the WebView and Tauri IPC/channel boundary from the hot path.
3. Keep the GGUF model loaded in-process between requests.
4. Preserve local-only Translate and Rewrite behavior.
5. Preserve selection capture, Double-Ctrl activation, replacement, tray, history, settings, and model management.
6. Support automatic GPU backend selection with safe CPU fallback.
7. Keep Windows x64 and Windows ARM64 release support.
8. Keep Microsoft Store packaging and GitHub release automation.
9. Add reproducible performance measurements so regressions are visible in CI or release validation.

## 3. Non-goals

The migration will not:

- redesign the public landing page;
- introduce cloud inference;
- change the default model family solely because of the migration;
- retain Tauri as a second production runtime;
- add a .NET-to-Rust bridge to keep the current Rust application layer;
- adopt Native AOT in the first migration milestone;
- add unrelated product features while parity is still incomplete.

Native AOT may be evaluated after functional and performance parity because LLamaSharp and native runtime packaging must first be proven across x64 and ARM64.

## 4. Current architecture being replaced

The current application is split into:

- TypeScript/HTML/CSS rendered through a Tauri WebView;
- Tauri commands and channels for UI-to-Rust calls and streaming;
- Rust modules for actions, configuration, history, keyboard monitoring, model management, popover control, text replacement, screenshots, selection capture, selection monitoring, and tray behavior;
- `rs-llama` as the llama.cpp adapter;
- compile-time CUDA/Vulkan feature selection;
- Node/Vite/Rust GitHub Actions build steps.

The existing model engine already keeps a loaded model in memory. That behavior must be preserved.

## 5. Target architecture

```text
Butchi.exe (.NET 10)
|
+-- Butchi.App
|   +-- Avalonia application lifecycle
|   +-- PopoverWindow
|   +-- SettingsWindow
|   +-- History UI
|   +-- Tray integration
|   +-- ViewModels
|
+-- Butchi.Core
|   +-- TranslateService
|   +-- RewriteService
|   +-- AutomationCoordinator
|   +-- SelectionCoordinator
|   +-- Prompt/profile logic
|   +-- Domain models and interfaces
|
+-- Butchi.Inference
|   +-- IInferenceEngine
|   +-- LLamaSharpInferenceEngine
|   +-- InferenceScheduler
|   +-- Backend selection
|   +-- Model download/load/status
|
+-- Butchi.Platform.Windows
|   +-- Selection capture
|   +-- Windows UI Automation
|   +-- guarded clipboard fallback
|   +-- Double-Ctrl monitor
|   +-- selected-text replacement
|   +-- cursor/window positioning
|
+-- Butchi.Infrastructure
    +-- configuration persistence
    +-- SQLite history
    +-- model files/downloads
    +-- local data deletion
```

### Project layout

```text
src/
  Butchi.App/
  Butchi.Core/
  Butchi.Inference/
  Butchi.Platform.Windows/
  Butchi.Infrastructure/

tests/
  Butchi.Core.Tests/
  Butchi.Inference.Tests/
  Butchi.Platform.Windows.Tests/
  Butchi.App.Tests/
```

`Butchi.Core` must not depend on Avalonia, LLamaSharp, or direct Win32 APIs.

## 6. UI and popover design

The popover is a native Avalonia `Window`, not a WebView.

Required behavior:

- borderless;
- topmost;
- not shown in the taskbar;
- cursor-adjacent positioning;
- reusable rather than recreated per selection;
- immediate show with captured text before inference completes;
- automatic vertical growth as streamed content arrives;
- bounded maximum dimensions with scrolling only when necessary;
- system/light/dark theme support;
- Escape closes the popover;
- existing auto-hide behavior is preserved;
- interaction cancels or extends auto-hide as appropriate;
- the window must avoid stealing or destroying the user's original selection before replacement metadata is captured.

The application creates the popover during startup and reuses it. Showing the popover must not trigger application or WebView initialization work.

Streaming token updates are delivered directly to the ViewModel and marshalled to the Avalonia UI thread. UI updates should be batched when token frequency is high enough to cause excessive layout work.

## 7. Selection capture and replacement

Windows behavior is retained behind explicit interfaces.

```text
ISelectionCaptureService
ISelectionMonitor
IGlobalShortcutMonitor
ITextReplacementService
ICursorPositionService
```

Capture order remains:

1. Windows UI Automation where supported.
2. Guarded clipboard fallback when UI Automation cannot obtain selected text.

Activation modes remain:

- supported mouse selection automation;
- Double-Ctrl for keyboard-selected text.

Before showing the popover, Butchi records enough information to target the originating application for optional replacement.

Manual input remains supported but replacement is disabled when there is no external selection target.

## 8. Inference design

### 8.1 Interface

Inference is isolated behind:

```text
IInferenceEngine
  LoadAsync(...)
  UnloadAsync(...)
  GenerateAsync(...)
  GetStatusAsync(...)
  GetDevicesAsync(...)
```

Generation returns streamed text using an async stream or equivalent callback abstraction owned by `Butchi.Core`, not LLamaSharp-specific types.

### 8.2 Model lifetime

The GGUF model is loaded once and retained in memory until one of these conditions occurs:

- user changes model;
- backend configuration requires reload;
- context-affecting configuration requires reload;
- user explicitly unloads or deletes local AI data;
- application exits.

A normal Translate or Rewrite request must never reload the model.

### 8.3 Prompt compatibility

Migration initially preserves current prompt semantics, including the existing Qwen-style chat framing where required by the selected model.

Prompt profiles, custom system prompts, target language, temperature, maximum tokens, and deterministic/default seed behavior are migrated before any prompt redesign is considered.

### 8.4 Cancellation

Every generation operation accepts cancellation. Starting a new selection/run invalidates obsolete UI updates and cancels work where the inference backend safely supports cancellation.

Closing the popover does not unload the model.

## 9. GPU/backend strategy

The user-facing backend preference remains:

- Auto
- GPU
- CPU

In Auto mode, Butchi attempts the best packaged backend available for the current machine and falls back safely.

For Windows x64 the intended preference order is:

```text
CUDA -> Vulkan -> CPU
```

For architectures or packages where CUDA is unavailable:

```text
Vulkan -> CPU
```

The exact LLamaSharp native backend packages must be validated during implementation for Windows x64 and Windows ARM64. Backend discovery must report what Butchi actually loaded, rather than merely what the user requested.

Model status exposes at least:

- model path/name;
- loaded state;
- selected preference;
- actual backend;
- detected device;
- GPU layer count;
- context size;
- load failure/fallback reason when applicable.

A failed Auto GPU load falls back to CPU. An explicit GPU preference surfaces an actionable error if no supported GPU backend can load the model.

## 10. Translate and Rewrite scheduling

Butchi currently supports automatically running Translate and Rewrite from one selection. The new architecture preserves this behavior while avoiding unnecessary VRAM contention.

An `InferenceScheduler` owns execution policy.

Rules:

1. It never starts a second model load for another action.
2. It may serialize Translate and Rewrite when memory pressure or backend constraints make parallel contexts unsafe.
3. It may allow parallel inference only when benchmarked and proven beneficial for the selected backend/device.
4. UI cards can independently show queued, generating, complete, or error states.
5. If only one automation action is enabled, existing Copy/Replace automation behavior remains.

Initial implementation should prefer correctness and predictable memory use over speculative parallel generation.

## 11. Configuration and history

Existing settings are migrated to .NET models with backward-compatible import where practical.

Settings include:

- Translate enabled;
- Rewrite enabled;
- result action;
- target language and favorites;
- system/custom prompts;
- prompt profiles;
- model repository/file;
- model/backend preference;
- GPU layer configuration;
- context/max token settings;
- temperature;
- popover auto-hide timing;
- history retention;
- appearance.

History remains local SQLite storage. Existing history data should be migrated or reused if schema compatibility can be retained without risky conversion. If a schema migration is required, it must be transactional and backed up before destructive changes.

## 12. Model download and local-data management

Model downloads remain explicit user actions and continue to use the existing application-data model location where feasible so users do not have to download the same GGUF again after upgrading.

Requirements:

- resumable behavior is optional for the first migration milestone;
- incomplete downloads use a temporary file;
- completed downloads are atomically promoted;
- UI remains responsive during download and load;
- model download success and model load success are reported separately;
- deleting local AI data unloads the model before deleting model files;
- selected text, prompts, history, and generations remain local.

## 13. Error handling

Errors are grouped into user-actionable categories:

- selection capture failure;
- replacement failure;
- model missing;
- model download failure;
- model load failure;
- unsupported GPU backend;
- GPU load failure with CPU fallback;
- inference failure;
- configuration/history persistence failure.

The popover should show concise errors without crashing the tray application. Detailed diagnostics go to structured local logs suitable for troubleshooting, with no prompt or selected-text content logged by default.

## 14. Performance requirements

The old Tauri application is the baseline. The same model, quantization, prompt, context, generation settings, GPU layer count, and backend must be used where technically possible.

Measurements include:

- cold application startup;
- warm application startup where applicable;
- selection captured -> popover visible;
- popover visible -> inference request dispatched;
- inference dispatch -> first token;
- tokens per second;
- UI responsiveness while tokens stream;
- idle process memory;
- loaded-model process memory;
- VRAM use;
- model load time.

Acceptance gates:

1. Selection captured -> reusable popover visible: target under 50 ms on the reference Windows machine.
2. Popover visible -> inference request dispatch: target under 30 ms excluding model load.
3. First-token latency must not regress materially when using equivalent inference settings.
4. Tokens/sec must not regress materially when using equivalent backend settings.
5. Streaming must not cause visible UI freezing or repeated expensive window recreation.
6. Model memory/VRAM use must not regress enough to make the default supported model unusable on previously supported hardware.
7. If raw inference is slower through LLamaSharp, the migration cannot be declared complete until the cause is understood and either fixed or explicitly accepted with evidence that total user-perceived latency still improves.

Performance measurements should be emitted in a machine-readable form so release comparisons are possible.

## 15. Testing strategy

### Unit tests

`Butchi.Core.Tests` covers:

- action parsing;
- prompt construction;
- automation decisions;
- backend preference policy;
- result action rules;
- history retention rules;
- popover state transitions independent of Avalonia controls.

### Inference tests

`Butchi.Inference.Tests` covers:

- backend policy;
- model lifetime/reuse;
- cancellation;
- streaming aggregation;
- fallback behavior using injectable abstractions.

A small GGUF smoke test may run separately where CI hardware/runtime makes this practical.

### Windows integration tests

Cover:

- global Double-Ctrl detection logic;
- selection capture adapters;
- clipboard fallback safety;
- replacement targeting;
- cursor-aware popover positioning;
- single-instance behavior.

Tests that cannot run reliably on every hosted runner must be isolated and clearly reported rather than silently skipped as if they passed.

### UI tests/screenshots

The existing deterministic screenshot concept is retained for the native Avalonia UI. CI produces canonical light/dark popover and settings screenshots from the real application UI.

## 16. CI and release migration

The new CI replaces Node/Vite/Rust build checks for the application runtime with .NET checks.

PR pipeline:

```text
dotnet restore
-> build x64
-> build ARM64
-> tests
-> analyzer/format enforcement
-> Windows integration tests
-> packaging smoke checks
-> deterministic Avalonia screenshots
```

Tag pipeline:

```text
publish win-x64
-> publish win-arm64
-> build installer/MSIX artifacts
-> GitHub Release
-> existing Microsoft Store submission path adapted to new package output
```

GitHub Pages remains independent and should not be rewritten as part of the runtime migration.

Store identity, package naming, signing, and Partner Center expectations must remain compatible with the existing Microsoft Store listing.

## 17. Migration phases

### Phase 1: Foundation

Create the .NET solution/projects, dependency boundaries, test projects, and CI build without changing the shipping application.

### Phase 2: Inference parity

Implement LLamaSharp engine, model loading, streaming, backend policy, model status, and benchmark harness. Compare with current `rs-llama` using equivalent settings.

### Phase 3: Native popover

Implement the reusable Avalonia popover and streaming ViewModel. Validate show latency and auto-sizing.

### Phase 4: Windows interaction

Port selection capture, selection monitoring, Double-Ctrl, replacement, cursor positioning, tray, and single-instance behavior.

### Phase 5: Settings/history/model manager

Port settings, prompt profiles, model download/load controls, history, retention, themes, and local-data deletion.

### Phase 6: Automation parity

Restore Translate + Rewrite auto-run behavior, result actions, favorite languages, reruns, cancellation, and scheduler policy.

### Phase 7: Packaging and screenshots

Adapt x64/ARM64 builds, GitHub releases, deterministic screenshots, MSIX/Store packaging, and documentation.

### Phase 8: Performance and parity gate

Run old-vs-new feature checklist and benchmarks. Resolve regressions before cutover.

### Phase 9: Cutover

Make Avalonia the production runtime. Only after successful cutover remove obsolete Tauri, Rust application, Vite, TypeScript, HTML, and Node build files from the runtime project.

## 18. Cutover and rollback rules

Tauri remains buildable on the migration branch until the Avalonia implementation passes the parity and performance gates.

Do not delete the old runtime early.

Cutover requires:

- feature parity checklist passed;
- x64 build passed;
- ARM64 build passed;
- release package produced;
- Store packaging validated;
- selection/Double-Ctrl/replacement validated on Windows;
- default model download/load/generate validated;
- performance comparison recorded;
- no material tokens/sec regression without explicit acceptance;
- no critical UI responsiveness regression.

If a release-blocking regression appears after the Avalonia cutover but before the first stable release, the branch can revert to the last Tauri-capable commit without data loss because configuration/history/model migration must be non-destructive until stability is proven.

## 19. Definition of done

The migration is done when Butchi ships as a .NET 10 Avalonia application using LLamaSharp for local llama.cpp inference, the Tauri runtime is no longer required, all existing user-visible workflows are preserved, Windows x64/ARM64 packaging works, Microsoft Store automation is compatible, and the recorded performance gates pass.

## 20. Implementation constraints

Implementation follows Superpowers planning and TDD after this design is reviewed and approved.

The implementation plan must break the migration into independently verifiable tasks and preserve a working baseline throughout. No production implementation begins from this design document alone until the written-spec review gate is approved.