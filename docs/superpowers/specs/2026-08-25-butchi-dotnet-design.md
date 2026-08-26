# Butchi .NET 10 + Avalonia + LLamaSharp Design

Date: 2026-08-25
Status: Approved
Reference implementation: `dhhieu113pro/butchi`

## Goal

Build Butchi as a Windows-first .NET 10 application using Avalonia and LLamaSharp, preserving the existing Butchi user workflows while improving UI responsiveness and simplifying the runtime architecture.

## Architecture

```text
Butchi.exe
|
+-- Butchi.App                 Avalonia lifecycle, reusable popover, Settings, History, tray
+-- Butchi.Core                actions, prompts, automation, scheduling, domain interfaces
+-- Butchi.Inference           LLamaSharp, model lifetime, backend selection, model management
+-- Butchi.Platform.Windows    UI Automation, clipboard fallback, Double-Ctrl, replacement, positioning
+-- Butchi.Infrastructure      config.json, SQLite history, local paths/logging
```

`Butchi.Core` must not reference Avalonia, LLamaSharp, or Win32 implementation types.

## Required behavior

Preserve from the reference Butchi app: automatic supported mouse-selection activation, Double-Ctrl keyboard activation, Windows UI Automation capture with guarded clipboard fallback, Translate and Rewrite streaming, Copy/Replace/None result actions, editable prompts and prompt profiles, favorite target languages, searchable local history and retention, System/Light/Dark theme, model setup/download/load, Auto/GPU/CPU preference, and local-data deletion.

The popover is a pre-created reusable native Avalonia window: borderless, topmost, excluded from taskbar, cursor-adjacent, immediate show, vertical auto-growth while bounded by a maximum size, and no per-selection WebView/window creation.

## Inference

`IInferenceEngine` owns load/unload/status/device discovery and async streamed generation. Model weights remain loaded between normal requests. Model reload occurs only when model/backend/context-affecting configuration changes, explicit unload/data deletion, or process exit.

Initial scheduler serializes Translate and Rewrite inference for predictable VRAM use. Parallel contexts are not enabled unless benchmarks prove a benefit without unsafe memory pressure.

Auto backend order on Windows x64: CUDA -> Vulkan -> CPU. Where CUDA is unavailable: Vulkan -> CPU. Explicit GPU failure surfaces an actionable error instead of silently choosing CPU.

## Persistence

Use `%APPDATA%/butchi`, camelCase `config.json`, the existing model path convention, and the existing `history.db` schema where compatible so a later migration from the old app can reuse user data/models.

Selected text and prompts remain local and must not be written to diagnostic logs by default.

## Platforms

Required release targets: Windows x64 and Windows ARM64. Native LLamaSharp backend packages must be validated for both. Native AOT is excluded from the first implementation.

## Performance gates

The reference Tauri app is the baseline using the same model, quantization, prompt, context, generation settings, GPU layers, backend, and machine.

- selection captured -> reusable popover visible: target < 50 ms;
- popover visible -> inference dispatch: target < 30 ms excluding model load;
- median first-token latency across >=5 equivalent warm runs: no more than 5% slower, unless total selection-to-first-token is >=15% faster;
- median tokens/sec across >=5 equivalent warm runs: no more than 5% slower;
- loaded-model RAM/VRAM: no more than 10% above baseline without explicit acceptance;
- no visible streaming UI freeze or per-run window recreation.

## Testing

Production behavior is implemented TDD-first. Unit tests cover Core rules and persistence. Inference tests cover backend policy, persistent model lifetime, streaming, cancellation, and fallback. Windows tests cover capture order, clipboard restoration, Double-Ctrl, replacement targeting, cursor positioning, and single-instance behavior. CI must produce deterministic real Avalonia screenshots and build/publish smoke checks for win-x64 and win-arm64.

## Non-goals

No cloud inference, no landing-page work, no unrelated features, no .NET-to-Rust bridge, no Native AOT during the initial implementation.
