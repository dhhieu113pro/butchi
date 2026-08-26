# Butchi .NET Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. TDD is mandatory for production behavior.

**Goal:** Build the greenfield .NET 10 + Avalonia + LLamaSharp Butchi implementation and prove feature/performance parity against `dhhieu113pro/butchi`.

**Architecture:** Core owns behavior/interfaces; Inference wraps LLamaSharp; Platform.Windows owns native Windows behavior; Infrastructure owns persistence; App is Avalonia. Keep files small and dependency direction one-way toward Core.

**Tech Stack:** .NET 10, C# 14, Avalonia, CommunityToolkit.Mvvm, LLamaSharp, Microsoft.Data.Sqlite, xUnit, Microsoft.Extensions.DependencyInjection.

**Spec:** `docs/superpowers/specs/2026-08-25-butchi-dotnet-design.md`

## Global Constraints

- Core must not reference Avalonia, LLamaSharp, or Win32 implementation types.
- Preserve existing Butchi defaults/config/history/model path compatibility.
- Keep one model loaded across normal requests.
- Auto backend: CUDA -> Vulkan -> CPU on x64; Vulkan -> CPU where CUDA is unavailable.
- Serialize Translate + Rewrite inference initially.
- Windows x64 and ARM64 are required.
- No Native AOT initially.
- No selected text/prompt content in logs by default.
- Every production behavior starts with a failing test.

---

### Task 1: .NET solution and architecture boundaries

Create `Butchi.slnx`, shared build/package props, projects `Butchi.Core`, `Butchi.Inference`, `Butchi.Infrastructure`, `Butchi.Platform.Windows`, `Butchi.App`, corresponding xUnit projects, and CI. First test asserts Core has no Avalonia/LLamaSharp/Platform.Windows references. Run RED before creating Core, then GREEN. CI builds/tests and publish-smokes win-x64/win-arm64.

### Task 2: Core behavior parity

TDD-port current defaults, `TextAction`, `ResultAction`, `BackendPreference`, prompt construction, target-language normalization, Translate/Rewrite enablement and automatic-result rules from the reference Butchi Rust logic.

### Task 3: Persistence compatibility

TDD-implement `%APPDATA%/butchi`, model path `/ -> __`, camelCase `config.json`, existing SQLite `history` schema/indexes/WAL, search/filter/limit/retention, and non-destructive legacy JSON history import.

### Task 4: Inference contracts and backend resolver

Define `IInferenceEngine`, request/status/device types. TDD backend resolution for Auto/GPU/CPU and x64/ARM64 fallback semantics without LLamaSharp implementation details leaking into Core.

### Task 5: LLamaSharp persistent inference

TDD model lifetime/reuse, context sizing, streaming, cancellation, reload triggers, seed/temperature/max tokens, and actual backend status. Validate native dependencies for win-x64 and win-arm64.

### Task 6: Model catalog/download/local-data management

TDD current model catalog, atomic temporary downloads, progress/cancellation, separate download/load status, unload-before-delete, history clear, model directory recreation.

### Task 7: Scheduler and automation

TDD serialized Translate/Rewrite scheduling, independent states, cancellation of obsolete runs, and Copy/Replace/None behavior including manual-input replacement prohibition.

### Task 8: Native Avalonia popover

TDD ViewModel state/run IDs and UI batching. Build a single reusable borderless/topmost/taskbar-hidden cursor-adjacent auto-growing window with bounded scrolling, themes, Escape, auto-hide, favorite-language rerun, and streaming updates.

### Task 9: Windows interaction layer

TDD and port UI Automation -> guarded clipboard fallback, clipboard restore, mouse selection monitoring, Double-Ctrl, captured replacement target, replacement, cursor clamping/flipping, tray and single-instance behavior.

### Task 10: Settings/history/tray/model manager UI

TDD settings save/reload rules, history operations, model status/backend/device display, first-run model setup, theme switching, prompt/profile/language controls, and DI composition with one inference-engine singleton.

### Task 11: Diagnostics, screenshots, packaging

TDD privacy-safe logging/error mapping. Add deterministic real Avalonia screenshot mode/CI. Publish smoke `win-x64` and `win-arm64`; then adapt GitHub Release/MSIX/Store packaging while preserving Store identity requirements from the reference app.

### Task 12: Parity and performance gate

Run all automated tests, Windows workflow checklist, real ARM64 validation where required, and >=5-run old-vs-new benchmark comparison. Fix regressions TDD-first. Do not declare the new repository production-ready until every required gate passes.

---

## Execution rule

Work exactly one task at a time. Each task ends only after its relevant CI is green and the PR contains independently reviewable evidence. Do not skip RED/GREEN verification.
