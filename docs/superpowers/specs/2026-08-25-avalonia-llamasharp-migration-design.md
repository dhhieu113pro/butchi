# Butchi Avalonia + LLamaSharp Migration Design

Date: 2026-08-25
Status: Approved 2026-08-25; implementation planning in progress
Branch: `design/avalonia-llamasharp-migration`

The approved design migrates Butchi from Tauri + TypeScript + Rust + `rs-llama` to a Windows-first .NET 10 application built with Avalonia and LLamaSharp. The complete design approved in PR #8 defines the following binding requirements.

## Goals

- Improve perceived popover and streaming responsiveness.
- Remove WebView/Tauri IPC from the hot path.
- Keep one GGUF model loaded in-process between requests.
- Preserve local-only Translate and Rewrite, mouse-selection activation, Double-Ctrl, guarded clipboard fallback, replacement, tray, settings, history, model management, prompt profiles, languages, result actions, and themes.
- Preserve Windows x64 and Windows ARM64 release support and Microsoft Store packaging.
- Use reproducible old-vs-new performance measurements as a cutover gate.

## Target architecture

```text
Butchi.exe (.NET 10)
|
+-- Butchi.App                 Avalonia lifecycle, popover, settings, history UI, tray, ViewModels
+-- Butchi.Core                actions, prompts, automation, selection coordination, domain interfaces
+-- Butchi.Inference           LLamaSharp engine, scheduler, backend selection, model status
+-- Butchi.Platform.Windows    UI Automation, clipboard fallback, Double-Ctrl, replacement, positioning
+-- Butchi.Infrastructure      JSON configuration, SQLite history, model files/downloads
```

`Butchi.Core` must not depend on Avalonia, LLamaSharp, or direct Win32 APIs.

## Popover

The popover is a reusable Avalonia `Window`: borderless, topmost, excluded from the taskbar, cursor-adjacent, shown immediately with captured text, automatically grows vertically while bounded by a maximum size, supports system/light/dark appearance, Escape and auto-hide behavior, and does not destroy the originating selection before replacement metadata is captured. It is created once and reused; token updates are marshalled to the UI thread and batched if necessary to avoid excessive layout work.

## Windows interaction

Windows behavior is isolated behind `ISelectionCaptureService`, `ISelectionMonitor`, `IGlobalShortcutMonitor`, `ITextReplacementService`, and `ICursorPositionService`. Capture order remains Windows UI Automation followed by guarded clipboard fallback. Activation remains supported mouse selection plus Double-Ctrl. Manual input remains supported but cannot replace external text without a selection target.

## Inference

Inference is isolated behind `IInferenceEngine` with load, unload, streaming generation, status, and device discovery. Core-facing streaming types cannot expose LLamaSharp-specific types. A normal Translate or Rewrite request never reloads the model. Reload occurs only for model/backend/context-affecting configuration changes, explicit unload/data deletion, or application exit. Existing prompt semantics, profiles, target language, temperature, maximum tokens, and seed behavior are preserved. Generation accepts cancellation; closing the popover does not unload the model.

## GPU/backend strategy

User preference remains Auto/GPU/CPU. Windows x64 Auto preference is CUDA -> Vulkan -> CPU. Where CUDA is unavailable, preference is Vulkan -> CPU. Actual loaded backend/device is reported, not merely the requested preference. Auto falls back to CPU on GPU load failure; explicit GPU preference returns an actionable error if no supported GPU backend loads. LLamaSharp native packages must be validated for both Windows x64 and Windows ARM64.

## Scheduling

An `InferenceScheduler` owns Translate/Rewrite execution. It never causes a second model load. Initial implementation serializes generation for predictable memory use; parallel contexts are allowed only after device/backend benchmarks prove a benefit without unsafe VRAM pressure. Each result card independently exposes queued/generating/complete/error state.

## Persistence

The existing `%APPDATA%/butchi`-style application-data location, `config.json`, model directory layout, and `history.db` are reused where feasible so upgrades do not force model re-downloads or history loss. JSON property names remain compatible with the current camelCase format. History keeps the current SQLite schema and WAL behavior where compatible. Any destructive schema conversion must be transactional and backed up first.

## Model downloads and privacy

Downloads remain explicit user actions. Incomplete downloads use a temporary file and completed downloads are atomically promoted. Download and model-load status are reported separately. Deleting local AI data unloads the model before deleting files. Selected text, prompts, history, and generations remain local. Logs must not contain selected text or prompt content by default.

## Performance gates

The current Tauri application is the baseline. Compare the same model, quantization, prompt, context, generation settings, GPU layers, backend, and reference Windows machine where technically possible. Record cold/warm startup, selection-to-popover, popover-to-dispatch, first-token latency, tokens/sec, streaming responsiveness, idle/loaded RAM, VRAM, and model-load time.

Cutover targets:

- selection captured -> reusable popover visible: < 50 ms target;
- popover visible -> inference dispatch: < 30 ms target excluding model load;
- median first-token latency over at least five equivalent warm runs: no more than 5% slower than Tauri unless total selection-to-first-token latency is at least 15% faster;
- median tokens/sec over at least five equivalent warm runs: no more than 5% slower than Tauri;
- loaded-model RAM and VRAM: no more than 10% above Tauri unless the default model remains comfortably usable and the increase is documented/approved;
- no visible UI freezing or per-run window recreation.

Machine-readable benchmark output is required. If LLamaSharp inference is slower, cutover is blocked until the cause is fixed or an explicit evidence-based exception is approved.

## Testing and CI

Use TDD for production behavior. Core tests cover actions, prompts, automation, backend policy, result actions, retention, and popover state. Inference tests cover backend policy, model reuse, cancellation, streaming, and fallback. Windows integration tests cover Double-Ctrl logic, selection capture, clipboard safety, replacement targeting, cursor positioning, and single-instance behavior. Deterministic CI screenshots must come from the real Avalonia UI.

PR CI builds/tests Windows x64 and ARM64, runs analyzers/format checks, integration tests that are reliable on hosted runners, packaging smoke checks, and deterministic screenshots. Tag CI publishes x64/ARM64, builds release/MSIX artifacts, publishes GitHub Release assets, and adapts the existing Microsoft Store submission path while preserving Store identity/signing/package expectations. GitHub Pages remains independent.

## Migration order

1. Foundation: .NET solution, boundaries, tests, CI without replacing the shipping Tauri app.
2. Inference parity: LLamaSharp, model lifetime, streaming, backend policy/status, benchmark harness.
3. Native popover: reusable Avalonia popover and streaming ViewModel.
4. Windows interaction: selection, monitors, Double-Ctrl, replacement, cursor positioning, tray, single instance.
5. Settings/history/model manager: persistence, profiles, downloads, retention, themes, local-data deletion.
6. Automation parity: Translate + Rewrite auto-run, result actions, favorites, reruns, cancellation, scheduler.
7. Packaging/screenshots: x64/ARM64 release, MSIX/Store, real Avalonia screenshots, documentation.
8. Performance/parity gate: old-vs-new checklist and benchmarks; resolve regressions.
9. Cutover: make Avalonia production runtime, then remove obsolete Tauri/Rust/Vite/TypeScript/HTML/Node runtime files.

## Rollback and definition of done

Tauri remains buildable until Avalonia passes feature parity, x64/ARM64 builds, release and Store packaging, Windows interaction validation, model download/load/generate validation, and recorded performance gates. Persistence migration remains non-destructive until stability is proven. The migration is done only when Butchi ships on .NET 10 + Avalonia + LLamaSharp with all current user workflows preserved, Tauri no longer required, packaging working, and performance gates passed.

## Non-goals

Do not redesign the landing page, add cloud inference, change model family merely because of migration, keep a production .NET-to-Rust bridge, enable Native AOT in the first milestone, or add unrelated features before parity.