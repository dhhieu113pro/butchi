# Butchi Avalonia + LLamaSharp Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Butchi's Tauri/Rust/WebView runtime with a faster Windows-first .NET 10 + Avalonia + LLamaSharp application while preserving every current user workflow and proving the cutover with old-vs-new performance measurements.

**Architecture:** Keep the existing Tauri application buildable while a parallel .NET solution is built under `src-dotnet/`. `Butchi.Core` owns behavior and interfaces, `Butchi.Inference` wraps LLamaSharp, `Butchi.Platform.Windows` owns Win32/UI Automation behavior, `Butchi.Infrastructure` reuses current config/history/model storage, and `Butchi.App` is the Avalonia tray/popover/settings application. Only after parity, packaging, and performance gates pass does the final task delete the old runtime.

**Tech Stack:** .NET 10, C# 14, Avalonia, CommunityToolkit.Mvvm, LLamaSharp, Microsoft.Data.Sqlite, xUnit, Microsoft.Extensions.DependencyInjection, System.Text.Json, Windows UI Automation/Win32 P/Invoke, GitHub Actions, MSIX.

**Spec:** `docs/superpowers/specs/2026-08-25-avalonia-llamasharp-migration-design.md`

## Global Constraints

- `Butchi.Core` must not reference Avalonia, LLamaSharp, or direct Win32 APIs.
- Preserve `%APPDATA%/butchi`, `config.json`, existing model paths, and `history.db` where compatible.
- Preserve current camelCase config compatibility and current Qwen prompt semantics before redesigning prompts.
- A normal Translate/Rewrite request must never reload model weights.
- Auto backend preference is CUDA -> Vulkan -> CPU on Windows x64, Vulkan -> CPU where CUDA is unavailable.
- Initial Translate + Rewrite generation is serialized; parallel contexts require later benchmark evidence.
- No selected text or prompt content in logs by default.
- Keep Tauri buildable until the final cutover task.
- Windows x64 and ARM64 remain required release targets.
- No Native AOT in this migration.
- No landing-page redesign or unrelated product features.
- TDD is mandatory: write each behavior test first, run it red, implement minimally, run green, then commit.
- Cutover gates: popover target <50 ms; dispatch target <30 ms excluding model load; median first-token no >5% regression unless end-to-end improves >=15%; median tokens/sec no >5% regression; loaded RAM/VRAM no >10% regression without explicit acceptance.

---

## File Structure

New runtime files are intentionally parallel to the current Tauri files until cutover.

```text
Butchi.slnx
Directory.Build.props
Directory.Packages.props
src-dotnet/
  Butchi.Core/
    Butchi.Core.csproj
    Actions/TextAction.cs
    Actions/ProcessRequest.cs
    Actions/ProcessResult.cs
    Configuration/AppConfig.cs
    Configuration/BackendPreference.cs
    Configuration/ResultAction.cs
    Inference/IInferenceEngine.cs
    Inference/InferenceRequest.cs
    Inference/InferenceStatus.cs
    Inference/BackendDevice.cs
    Automation/AutomationCoordinator.cs
    Automation/InferenceScheduler.cs
    Selection/SelectionSnapshot.cs
    Selection/ISelectionCaptureService.cs
    Selection/ISelectionMonitor.cs
    Selection/IGlobalShortcutMonitor.cs
    Selection/ITextReplacementService.cs
    Selection/ICursorPositionService.cs
    Prompts/PromptBuilder.cs
    History/HistoryEntry.cs
    History/IHistoryStore.cs
  Butchi.Inference/
    Butchi.Inference.csproj
    LLamaSharpInferenceEngine.cs
    BackendResolver.cs
    ModelCatalog.cs
    ModelDownloader.cs
  Butchi.Infrastructure/
    Butchi.Infrastructure.csproj
    AppPaths.cs
    JsonConfigStore.cs
    SqliteHistoryStore.cs
  Butchi.Platform.Windows/
    Butchi.Platform.Windows.csproj
    Selection/WindowsSelectionCaptureService.cs
    Selection/WindowsSelectionMonitor.cs
    Selection/DoubleCtrlMonitor.cs
    Selection/WindowsTextReplacementService.cs
    Windowing/WindowsCursorPositionService.cs
    Windowing/SingleInstanceGuard.cs
  Butchi.App/
    Butchi.App.csproj
    Program.cs
    App.axaml
    App.axaml.cs
    Views/PopoverWindow.axaml
    Views/PopoverWindow.axaml.cs
    Views/SettingsWindow.axaml
    Views/SettingsWindow.axaml.cs
    Views/HistoryWindow.axaml
    ViewModels/PopoverViewModel.cs
    ViewModels/SettingsViewModel.cs
    ViewModels/HistoryViewModel.cs
    Services/TrayService.cs
    Services/PopoverController.cs
    Services/UiBatcher.cs
    Assets/...
  Butchi.Benchmarks/
    Butchi.Benchmarks.csproj
    Program.cs
    BenchmarkResult.cs
    JsonBenchmarkWriter.cs
tests-dotnet/
  Butchi.Core.Tests/
  Butchi.Inference.Tests/
  Butchi.Infrastructure.Tests/
  Butchi.Platform.Windows.Tests/
  Butchi.App.Tests/
.github/workflows/
  dotnet-ci.yml
  dotnet-screenshots.yml
  release.yml                 # modified at packaging phase
  publish-windows-store.yml   # modified at packaging phase
scripts/
  compare-performance.ps1
```

---

### Task 1: Establish the .NET 10 solution and dependency boundaries

**Files:**
- Create: `Butchi.slnx`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `src-dotnet/Butchi.Core/Butchi.Core.csproj`
- Create: `src-dotnet/Butchi.Inference/Butchi.Inference.csproj`
- Create: `src-dotnet/Butchi.Infrastructure/Butchi.Infrastructure.csproj`
- Create: `src-dotnet/Butchi.Platform.Windows/Butchi.Platform.Windows.csproj`
- Create: `src-dotnet/Butchi.App/Butchi.App.csproj`
- Create: `src-dotnet/Butchi.Benchmarks/Butchi.Benchmarks.csproj`
- Create: `tests-dotnet/Butchi.Core.Tests/Butchi.Core.Tests.csproj`
- Create: `tests-dotnet/Butchi.Inference.Tests/Butchi.Inference.Tests.csproj`
- Create: `tests-dotnet/Butchi.Infrastructure.Tests/Butchi.Infrastructure.Tests.csproj`
- Create: `tests-dotnet/Butchi.Platform.Windows.Tests/Butchi.Platform.Windows.Tests.csproj`
- Create: `tests-dotnet/Butchi.App.Tests/Butchi.App.Tests.csproj`
- Create: `tests-dotnet/Butchi.Core.Tests/ArchitectureTests.cs`
- Create: `.github/workflows/dotnet-ci.yml`

**Interfaces:**
- Produces project dependency direction: `App -> Core + Inference + Infrastructure + Platform.Windows`, `Inference -> Core`, `Infrastructure -> Core`, `Platform.Windows -> Core`; Core references none of the implementation projects.

- [ ] **Step 1: Write a failing architecture test**

```csharp
[Fact]
public void Core_must_not_reference_ui_inference_or_windows_projects()
{
    var references = typeof(Butchi.Core.Actions.TextAction).Assembly
        .GetReferencedAssemblies()
        .Select(x => x.Name)
        .ToArray();

    Assert.DoesNotContain("Avalonia", references);
    Assert.DoesNotContain("LLamaSharp", references);
    Assert.DoesNotContain("Butchi.Platform.Windows", references);
}
```

- [ ] **Step 2: Run it red because the solution/types do not exist**

Run: `dotnet test tests-dotnet/Butchi.Core.Tests/Butchi.Core.Tests.csproj --no-restore`
Expected: FAIL because the new project/type is absent.

- [ ] **Step 3: Create solution/projects with `net10.0`, nullable and warnings enabled**

Use `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>14.0</LangVersion>
  </PropertyGroup>
</Project>
```

Add only the dependency edges listed above. Put a minimal `TextAction` enum in Core so the architecture test has a stable anchor.

- [ ] **Step 4: Add parallel .NET CI without touching existing Tauri CI**

`dotnet-ci.yml` must restore, build, and test `Butchi.slnx` on `windows-latest`; add an ARM64 publish smoke command using `-r win-arm64 --self-contained true` once `Butchi.App` exists.

- [ ] **Step 5: Run green**

Run: `dotnet restore Butchi.slnx && dotnet build Butchi.slnx -c Release && dotnet test Butchi.slnx -c Release --no-build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Butchi.slnx Directory.Build.props Directory.Packages.props src-dotnet tests-dotnet .github/workflows/dotnet-ci.yml
git commit -m "build: add .NET 10 Avalonia migration foundation"
```

---

### Task 2: Port core configuration, actions, prompts, and automation rules

**Files:**
- Create: `src-dotnet/Butchi.Core/Actions/TextAction.cs`
- Create: `src-dotnet/Butchi.Core/Actions/ProcessRequest.cs`
- Create: `src-dotnet/Butchi.Core/Actions/ProcessResult.cs`
- Create: `src-dotnet/Butchi.Core/Configuration/AppConfig.cs`
- Create: `src-dotnet/Butchi.Core/Configuration/BackendPreference.cs`
- Create: `src-dotnet/Butchi.Core/Configuration/ResultAction.cs`
- Create: `src-dotnet/Butchi.Core/Prompts/PromptBuilder.cs`
- Create: `src-dotnet/Butchi.Core/Automation/AutomationCoordinator.cs`
- Create: `tests-dotnet/Butchi.Core.Tests/AppConfigTests.cs`
- Create: `tests-dotnet/Butchi.Core.Tests/PromptBuilderTests.cs`
- Create: `tests-dotnet/Butchi.Core.Tests/AutomationCoordinatorTests.cs`
- Reference behavior: `src-tauri/src/config.rs`, `src-tauri/src/core_logic.rs`, `src-tauri/src/actions.rs`

**Interfaces:**
- Produces: `AppConfig.Default`, `BackendPreference { Auto, Gpu, Cpu }`, `ResultAction { Copy, Replace, None }`, `PromptBuilder.Build(TextAction, string, AppConfig)`, `AutomationCoordinator.GetEnabledActions(AppConfig)`.

- [ ] **Step 1: Write failing compatibility tests for current defaults**

Assert defaults exactly preserve current values: Translate/Rewrite enabled, Vietnamese, `[Vietnamese, English]`, result `copy`, backend `auto`, `unsloth/Qwen3.5-0.8B-GGUF`, `Qwen3.5-0.8B-Q4_K_M.gguf`, max tokens 256, temperature 0.3, GPU layers 999, retention 30, hide 6 seconds.

- [ ] **Step 2: Run red**

Run: `dotnet test tests-dotnet/Butchi.Core.Tests --filter "AppConfigTests|PromptBuilderTests|AutomationCoordinatorTests"`
Expected: FAIL because the behavior is absent.

- [ ] **Step 3: Implement minimal immutable/domain models and normalization**

Use typed enums internally while keeping serializer-compatible string conversion in Infrastructure later. `PromptBuilder` must preserve current Qwen framing:

```text
<|im_start|>system
{system}<|im_end|>
<|im_start|>user
{user}<|im_end|>
<|im_start|>assistant
```

Translate user content must include the selected target language exactly as current action logic does; Rewrite preserves source language.

- [ ] **Step 4: Implement automation rules**

Return Translate then Rewrite when both are enabled. Expose `ShouldApplyAutomaticResult` only when exactly one action is enabled, preserving current Copy/Replace semantics.

- [ ] **Step 5: Run green**

Run: `dotnet test tests-dotnet/Butchi.Core.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src-dotnet/Butchi.Core tests-dotnet/Butchi.Core.Tests
git commit -m "feat: port Butchi core behavior to .NET"
```

---

### Task 3: Reuse current config, model paths, and SQLite history without data loss

**Files:**
- Create: `src-dotnet/Butchi.Core/History/HistoryEntry.cs`
- Create: `src-dotnet/Butchi.Core/History/IHistoryStore.cs`
- Create: `src-dotnet/Butchi.Infrastructure/AppPaths.cs`
- Create: `src-dotnet/Butchi.Infrastructure/JsonConfigStore.cs`
- Create: `src-dotnet/Butchi.Infrastructure/SqliteHistoryStore.cs`
- Create: `tests-dotnet/Butchi.Infrastructure.Tests/JsonConfigStoreTests.cs`
- Create: `tests-dotnet/Butchi.Infrastructure.Tests/SqliteHistoryStoreTests.cs`
- Reference: `src-tauri/src/config.rs`, `src-tauri/src/history.rs`

**Interfaces:**
- Produces: `AppPaths.DataDirectory`, `AppPaths.ModelsDirectory`, `AppPaths.ModelPath(repo,file)`, `JsonConfigStore.LoadAsync/SaveAsync`, `IHistoryStore.SearchAsync/AppendAsync/DeleteAsync/ClearAsync/ApplyRetentionAsync`.

- [ ] **Step 1: Write failing config compatibility test**

Create a temp `config.json` containing the current camelCase keys and assert C# loads it without changing defaults for omitted fields. Save and assert property names remain camelCase.

- [ ] **Step 2: Write failing history compatibility test**

Create the exact existing schema:

```sql
CREATE TABLE history (
 id TEXT PRIMARY KEY,
 ts INTEGER NOT NULL,
 action TEXT NOT NULL,
 source TEXT NOT NULL,
 result TEXT NOT NULL,
 message TEXT NOT NULL,
 target_language TEXT NULL
);
```

Insert rows and assert C# search orders by `ts DESC`, filters source/result case-insensitively, filters action, clamps limit 1..500, and preserves `target_language`.

- [ ] **Step 3: Run red**

Run: `dotnet test tests-dotnet/Butchi.Infrastructure.Tests`
Expected: FAIL.

- [ ] **Step 4: Implement storage compatibility**

Use `Environment.SpecialFolder.ApplicationData` + `butchi`; preserve repo sanitization `/ -> __`. Use `System.Text.Json` camelCase and defaults. Use `Microsoft.Data.Sqlite`, WAL, the existing table/index names, and retention semantics: `<0 forever`, `0 clear/disabled`, positive days cutoff.

- [ ] **Step 5: Add non-destructive legacy JSON history migration test and implementation**

Import `history.json` with `INSERT OR IGNORE`, then rename only after a successful transaction to `history.migrated.json`. On failure, leave the source file untouched.

- [ ] **Step 6: Run green**

Run: `dotnet test tests-dotnet/Butchi.Infrastructure.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src-dotnet/Butchi.Infrastructure src-dotnet/Butchi.Core/History tests-dotnet/Butchi.Infrastructure.Tests
git commit -m "feat: preserve Butchi settings and history storage"
```

---

### Task 4: Define inference contracts and backend resolution policy

**Files:**
- Create: `src-dotnet/Butchi.Core/Inference/IInferenceEngine.cs`
- Create: `src-dotnet/Butchi.Core/Inference/InferenceRequest.cs`
- Create: `src-dotnet/Butchi.Core/Inference/InferenceStatus.cs`
- Create: `src-dotnet/Butchi.Core/Inference/BackendDevice.cs`
- Create: `src-dotnet/Butchi.Inference/BackendResolver.cs`
- Create: `tests-dotnet/Butchi.Inference.Tests/BackendResolverTests.cs`

**Interfaces:**
- Produces:

```csharp
public interface IInferenceEngine : IAsyncDisposable
{
    Task LoadAsync(AppConfig config, CancellationToken cancellationToken);
    Task UnloadAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<string> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken);
    InferenceStatus GetStatus();
    IReadOnlyList<BackendDevice> GetDevices();
}
```

- [ ] **Step 1: Write failing backend policy tests**

Cases: x64 Auto + CUDA available => CUDA; x64 Auto CUDA fails + Vulkan available => Vulkan; Auto both fail => CPU; ARM64 without CUDA => Vulkan then CPU; explicit CPU never probes GPU; explicit GPU with no GPU backend => actionable failure, not silent CPU fallback.

- [ ] **Step 2: Run red**

Run: `dotnet test tests-dotnet/Butchi.Inference.Tests --filter BackendResolverTests`
Expected: FAIL.

- [ ] **Step 3: Implement pure `BackendResolver` with injectable probes**

Do not load LLamaSharp yet. Return an ordered list of backend attempts and a final resolution/failure reason so it is deterministic and unit-testable.

- [ ] **Step 4: Run green and commit**

Run: `dotnet test tests-dotnet/Butchi.Inference.Tests --filter BackendResolverTests`
Expected: PASS.

```bash
git add src-dotnet/Butchi.Core/Inference src-dotnet/Butchi.Inference/BackendResolver.cs tests-dotnet/Butchi.Inference.Tests
git commit -m "feat: define inference and backend contracts"
```

---

### Task 5: Implement persistent LLamaSharp model loading and streaming

**Files:**
- Create: `src-dotnet/Butchi.Inference/LLamaSharpInferenceEngine.cs`
- Create: `src-dotnet/Butchi.Inference/ModelCatalog.cs`
- Create: `tests-dotnet/Butchi.Inference.Tests/LLamaSharpInferenceEngineTests.cs`
- Modify: `Directory.Packages.props`
- Reference: `src-tauri/src/llm_engine.rs`

**Interfaces:**
- Consumes `IInferenceEngine`, `BackendResolver`, `AppConfig`.
- Produces one loaded LLamaSharp model instance reused across requests and streamed text chunks.

- [ ] **Step 1: Write failing model-lifetime tests around an injectable native/model adapter**

Assert two equivalent `GenerateAsync` calls cause one model load; model path/backend/context change causes unload+load; closing/cancelling a generation does not unload weights; explicit unload does.

- [ ] **Step 2: Write failing streaming/cancellation tests**

Fake executor emits `A`, `B`, `C`; assert async stream yields in order and aggregates `ABC`. Cancel after `B`; assert no later UI chunk and the engine remains loaded.

- [ ] **Step 3: Run red**

Run: `dotnet test tests-dotnet/Butchi.Inference.Tests --filter LLamaSharpInferenceEngineTests`
Expected: FAIL.

- [ ] **Step 4: Add LLamaSharp packages and implement engine**

Load GGUF using current context rule `max(maxTokens + 2048, 10000)`, requested GPU layers, temperature, max tokens, seed 42, and prompt from Core. Keep weights in a private loaded-model holder. Convert LLamaSharp output to `IAsyncEnumerable<string>` without exposing LLamaSharp types.

- [ ] **Step 5: Validate actual native package availability for win-x64 and win-arm64**

Run:

```powershell
dotnet publish src-dotnet/Butchi.App/Butchi.App.csproj -c Release -r win-x64 --self-contained true
dotnet publish src-dotnet/Butchi.App/Butchi.App.csproj -c Release -r win-arm64 --self-contained true
```

If a CUDA package has no ARM64 asset, package Vulkan/CPU for ARM64 and keep the resolver order required by the spec. Record exact package choices in `Directory.Packages.props`; do not invent an unsupported backend.

- [ ] **Step 6: Run green and commit**

Run: `dotnet test tests-dotnet/Butchi.Inference.Tests`
Expected: PASS.

```bash
git add Directory.Packages.props src-dotnet/Butchi.Inference tests-dotnet/Butchi.Inference.Tests
git commit -m "feat: add persistent LLamaSharp inference"
```

---

### Task 6: Add model catalog, download, atomic promotion, and local-data deletion

**Files:**
- Create: `src-dotnet/Butchi.Inference/ModelCatalog.cs`
- Create: `src-dotnet/Butchi.Inference/ModelDownloader.cs`
- Create: `tests-dotnet/Butchi.Inference.Tests/ModelDownloaderTests.cs`
- Modify: `src-dotnet/Butchi.Infrastructure/AppPaths.cs`

**Interfaces:**
- Produces current three model choices and `DownloadAsync(repo,file,progress,ct)` returning the final local path.

- [ ] **Step 1: Write failing catalog/path tests**

Assert current Qwen3.5 0.8B Q4/Q5 and Qwen3 0.6B Q4 entries and exact existing local path convention.

- [ ] **Step 2: Write failing atomic download test**

Use an injectable HTTP stream source. Assert download writes `<file>.download`, final file does not appear before completion, cancellation leaves no corrupted final file, and success atomically promotes to `.gguf`.

- [ ] **Step 3: Run red**

Run: `dotnet test tests-dotnet/Butchi.Inference.Tests --filter "ModelDownloader|ModelCatalog"`
Expected: FAIL.

- [ ] **Step 4: Implement minimal downloader and clear-data coordinator**

Report download and load as separate operations. Clear-data flow must call `IInferenceEngine.UnloadAsync`, clear history, delete model directory, then recreate it.

- [ ] **Step 5: Run green and commit**

```bash
dotnet test tests-dotnet/Butchi.Inference.Tests
git add src-dotnet/Butchi.Inference src-dotnet/Butchi.Infrastructure tests-dotnet/Butchi.Inference.Tests
git commit -m "feat: add model management for Avalonia runtime"
```

---

### Task 7: Implement serialized inference scheduling and result automation

**Files:**
- Create: `src-dotnet/Butchi.Core/Automation/InferenceScheduler.cs`
- Create: `tests-dotnet/Butchi.Core.Tests/InferenceSchedulerTests.cs`
- Modify: `src-dotnet/Butchi.Core/Automation/AutomationCoordinator.cs`

**Interfaces:**
- Produces `RunAsync(IReadOnlyList<ProcessRequest>, CancellationToken)` with per-action queued/generating/completed/error events.

- [ ] **Step 1: Write failing scheduling tests**

With Translate + Rewrite enabled, assert one request generates at a time and Translate starts before Rewrite. Assert both use the same engine instance and no scheduler call invokes `LoadAsync` independently.

- [ ] **Step 2: Write failing result-action tests**

Exactly one enabled action + Copy => copy result; exactly one + Replace and external selection target => replace; both enabled => do not automatically copy/replace the first completion; manual input + Replace => return a user-facing unavailable state.

- [ ] **Step 3: Run red**

Run: `dotnet test tests-dotnet/Butchi.Core.Tests --filter "InferenceScheduler|AutomationCoordinator"`
Expected: FAIL.

- [ ] **Step 4: Implement serialized scheduler using `SemaphoreSlim(1,1)` and cancellation**

Do not add parallel inference configuration. Emit state transitions independently for each action.

- [ ] **Step 5: Run green and commit**

```bash
dotnet test tests-dotnet/Butchi.Core.Tests
git add src-dotnet/Butchi.Core tests-dotnet/Butchi.Core.Tests
git commit -m "feat: schedule local inference safely"
```

---

### Task 8: Build reusable native Avalonia popover with batched streaming updates

**Files:**
- Create: `src-dotnet/Butchi.App/Program.cs`
- Create: `src-dotnet/Butchi.App/App.axaml`
- Create: `src-dotnet/Butchi.App/App.axaml.cs`
- Create: `src-dotnet/Butchi.App/Views/PopoverWindow.axaml`
- Create: `src-dotnet/Butchi.App/Views/PopoverWindow.axaml.cs`
- Create: `src-dotnet/Butchi.App/ViewModels/PopoverViewModel.cs`
- Create: `src-dotnet/Butchi.App/Services/PopoverController.cs`
- Create: `src-dotnet/Butchi.App/Services/UiBatcher.cs`
- Create: `tests-dotnet/Butchi.App.Tests/PopoverViewModelTests.cs`
- Create: `tests-dotnet/Butchi.App.Tests/UiBatcherTests.cs`
- Reference UI behavior: `src/main.ts`, `src/styles.css`, `src-tauri/src/popover.rs`

**Interfaces:**
- Produces one process-lifetime `PopoverWindow`, `PopoverController.ShowSelectionAsync(SelectionSnapshot)`, and ViewModel result states `Idle/Queued/Generating/Success/Error`.

- [ ] **Step 1: Write failing ViewModel state tests**

Assert a new selection clears old results, sets source text immediately, starts enabled actions, appends streamed chunks, ignores chunks from an obsolete run ID, and Escape/close cancels the run without unloading inference.

- [ ] **Step 2: Write failing batching test**

Given rapid chunks within a 16 ms window, assert `UiBatcher` produces one combined UI update; a later chunk produces a second update.

- [ ] **Step 3: Run red**

Run: `dotnet test tests-dotnet/Butchi.App.Tests`
Expected: FAIL.

- [ ] **Step 4: Implement ViewModel/controller and one reusable window**

Configure Avalonia window: no decorations, topmost, taskbar false, size-to-content height with maximum height/width, scroll result area after cap. Do not instantiate a new window in `ShowSelectionAsync`.

- [ ] **Step 5: Port current result-card UX and auto-hide behavior**

Preserve selected text/manual input, Translate/Rewrite cards, favorite language rerun, Working/Generating/Done/Error states, pointer/focus interaction extension, 2..30 second configured hide delay, and Escape close.

- [ ] **Step 6: Run green and launch smoke test**

Run: `dotnet test tests-dotnet/Butchi.App.Tests && dotnet run --project src-dotnet/Butchi.App`
Expected: tests pass; native popover can be shown in a developer/screenshot mode without WebView.

- [ ] **Step 7: Commit**

```bash
git add src-dotnet/Butchi.App tests-dotnet/Butchi.App.Tests
git commit -m "feat: add native reusable Avalonia popover"
```

---

### Task 9: Port Windows selection capture and guarded clipboard fallback

**Files:**
- Create: `src-dotnet/Butchi.Core/Selection/SelectionSnapshot.cs`
- Create: `src-dotnet/Butchi.Core/Selection/ISelectionCaptureService.cs`
- Create: `src-dotnet/Butchi.Core/Selection/ISelectionMonitor.cs`
- Create: `src-dotnet/Butchi.Platform.Windows/Selection/WindowsSelectionCaptureService.cs`
- Create: `src-dotnet/Butchi.Platform.Windows/Selection/WindowsSelectionMonitor.cs`
- Create: `tests-dotnet/Butchi.Platform.Windows.Tests/WindowsSelectionCaptureServiceTests.cs`
- Reference: `src-tauri/src/selection.rs`, `src-tauri/src/selection_monitor.rs`

**Interfaces:**
- `CaptureAsync()` returns `SelectionSnapshot(Text, SourceWindowHandle, IsManualInput:false, CapturedAt)` and captures replacement target metadata before the popover takes focus.

- [ ] **Step 1: Write failing capture-order tests using injected UIA and clipboard adapters**

Assert UIA success never touches clipboard; UIA failure invokes guarded clipboard capture; both failures return a typed capture failure without crashing; empty/whitespace text is rejected.

- [ ] **Step 2: Write failing clipboard restoration test**

Seed clipboard content, simulate capture, and assert original clipboard content is restored even when capture throws.

- [ ] **Step 3: Run red**

Run: `dotnet test tests-dotnet/Butchi.Platform.Windows.Tests --filter WindowsSelectionCaptureServiceTests`
Expected: FAIL.

- [ ] **Step 4: Port the existing Windows behavior behind small adapters**

Keep UI Automation first and guarded clipboard fallback second. Do not introduce a new capture technique during migration.

- [ ] **Step 5: Run green and commit**

```bash
dotnet test tests-dotnet/Butchi.Platform.Windows.Tests
git add src-dotnet/Butchi.Core/Selection src-dotnet/Butchi.Platform.Windows tests-dotnet/Butchi.Platform.Windows.Tests
git commit -m "feat: port Windows selection capture"
```

---

### Task 10: Port Double-Ctrl activation, replacement, cursor positioning, and single instance

**Files:**
- Create: `src-dotnet/Butchi.Core/Selection/IGlobalShortcutMonitor.cs`
- Create: `src-dotnet/Butchi.Core/Selection/ITextReplacementService.cs`
- Create: `src-dotnet/Butchi.Core/Selection/ICursorPositionService.cs`
- Create: `src-dotnet/Butchi.Platform.Windows/Selection/DoubleCtrlMonitor.cs`
- Create: `src-dotnet/Butchi.Platform.Windows/Selection/WindowsTextReplacementService.cs`
- Create: `src-dotnet/Butchi.Platform.Windows/Windowing/WindowsCursorPositionService.cs`
- Create: `src-dotnet/Butchi.Platform.Windows/Windowing/SingleInstanceGuard.cs`
- Create: `tests-dotnet/Butchi.Platform.Windows.Tests/DoubleCtrlMonitorTests.cs`
- Create: `tests-dotnet/Butchi.Platform.Windows.Tests/TextReplacementTests.cs`
- Create: `tests-dotnet/Butchi.Platform.Windows.Tests/CursorPositionTests.cs`
- Reference: `src-tauri/src/keyboard_monitor.rs`, `src-tauri/src/replacement.rs`, `src-tauri/src/popover.rs`

**Interfaces:**
- Produces Double-Ctrl event, `ReplaceAsync(SelectionSnapshot,string,ct)`, cursor point, and one-instance activation signal.

- [ ] **Step 1: Write failing Double-Ctrl timing/state tests**

Use a fake clock/key stream. Assert two Ctrl presses inside the existing threshold activate once; unrelated keys reset; held/repeated Ctrl does not trigger repeatedly.

- [ ] **Step 2: Write failing replacement target tests**

Assert replacement targets the captured source window, not the Butchi popover; manual input is rejected; clipboard is restored after paste-based fallback.

- [ ] **Step 3: Write failing positioning tests**

Given cursor/work-area/window size, assert popover prefers below/right but clamps inside the monitor work area and flips when near bottom/right edges.

- [ ] **Step 4: Run red**

Run: `dotnet test tests-dotnet/Butchi.Platform.Windows.Tests`
Expected: FAIL.

- [ ] **Step 5: Implement ports and wire startup**

Use a named mutex or equivalent for single instance. Second launch signals/focuses Settings rather than starting duplicate monitors/model instances.

- [ ] **Step 6: Run green and commit**

```bash
dotnet test tests-dotnet/Butchi.Platform.Windows.Tests
git add src-dotnet/Butchi.Platform.Windows src-dotnet/Butchi.Core/Selection tests-dotnet/Butchi.Platform.Windows.Tests
git commit -m "feat: port Butchi Windows activation and replacement"
```

---

### Task 11: Build Settings, history, tray, themes, and dependency composition

**Files:**
- Create: `src-dotnet/Butchi.App/Views/SettingsWindow.axaml`
- Create: `src-dotnet/Butchi.App/Views/SettingsWindow.axaml.cs`
- Create: `src-dotnet/Butchi.App/Views/HistoryWindow.axaml`
- Create: `src-dotnet/Butchi.App/ViewModels/SettingsViewModel.cs`
- Create: `src-dotnet/Butchi.App/ViewModels/HistoryViewModel.cs`
- Create: `src-dotnet/Butchi.App/Services/TrayService.cs`
- Modify: `src-dotnet/Butchi.App/App.axaml.cs`
- Create: `tests-dotnet/Butchi.App.Tests/SettingsViewModelTests.cs`
- Create: `tests-dotnet/Butchi.App.Tests/HistoryViewModelTests.cs`
- Reference: `settings.html`, `src/settings.ts`, `src-tauri/src/tray.rs`

**Interfaces:**
- Produces full existing settings surface, searchable/deletable history, tray commands Open Settings/History/Exit, and System/Light/Dark theme.

- [ ] **Step 1: Write failing settings tests**

Assert save persists current settings, backend/model/context-affecting changes trigger inference reload exactly once, language-only/prompt-only changes do not reload weights, target language normalization rejects blank values, and hide seconds clamp 2..30.

- [ ] **Step 2: Write failing history tests**

Assert search/filter/delete/clear/retention calls map to `IHistoryStore` and UI refreshes after mutation.

- [ ] **Step 3: Run red**

Run: `dotnet test tests-dotnet/Butchi.App.Tests --filter "Settings|History"`
Expected: FAIL.

- [ ] **Step 4: Implement settings/history/tray with DI composition**

Register one singleton inference engine, scheduler, popover, Windows monitors, config/history stores, and model downloader. Start monitors only after single-instance ownership. If model is missing, open Settings on first launch.

- [ ] **Step 5: Preserve all current settings fields and model status**

Show requested preference plus actual backend/device, loaded/downloaded state, GPU layers, context, and fallback/error reason.

- [ ] **Step 6: Run full .NET tests and commit**

```bash
dotnet test Butchi.slnx -c Release
git add src-dotnet/Butchi.App tests-dotnet/Butchi.App.Tests
git commit -m "feat: complete Avalonia settings history and tray"
```

---

### Task 12: Add privacy-safe structured logging and user-actionable error mapping

**Files:**
- Create: `src-dotnet/Butchi.Core/Errors/ButchiError.cs`
- Create: `src-dotnet/Butchi.App/Services/ErrorPresenter.cs`
- Create: `src-dotnet/Butchi.Infrastructure/Logging/PrivacyLogSanitizer.cs`
- Create: `tests-dotnet/Butchi.Infrastructure.Tests/PrivacyLogSanitizerTests.cs`
- Create: `tests-dotnet/Butchi.App.Tests/ErrorPresenterTests.cs`

**Interfaces:**
- Produces typed categories: selection, replacement, model missing/download/load, unsupported GPU, GPU fallback, inference, persistence.

- [ ] **Step 1: Write failing privacy tests**

Pass a fake selected text and system prompt through error/log context and assert serialized logs contain operation IDs, category, exception type/message, backend/model metadata, but not selected text or prompt values.

- [ ] **Step 2: Write failing presentation tests**

Assert GPU Auto failure+CPU success presents a non-fatal fallback status; explicit GPU failure presents actionable Settings guidance; inference failure leaves tray app alive and result card in Error state.

- [ ] **Step 3: Run red, implement, run green**

Run: `dotnet test tests-dotnet/Butchi.Infrastructure.Tests tests-dotnet/Butchi.App.Tests --filter "Privacy|ErrorPresenter"`
Expected before implementation: FAIL; after: PASS.

- [ ] **Step 4: Commit**

```bash
git add src-dotnet/Butchi.Core/Errors src-dotnet/Butchi.Infrastructure/Logging src-dotnet/Butchi.App/Services tests-dotnet
git commit -m "feat: add privacy-safe diagnostics"
```

---

### Task 13: Add deterministic Avalonia screenshot mode and real UI screenshot CI

**Files:**
- Create: `src-dotnet/Butchi.App/Services/ScreenshotMode.cs`
- Create: `.github/workflows/dotnet-screenshots.yml`
- Modify: `src-dotnet/Butchi.App/App.axaml.cs`
- Modify: `docs/shots/popover-light.png` via CI artifact/update flow
- Modify: `docs/shots/popover-dark.png` via CI artifact/update flow
- Modify: `docs/shots/settings-light.png` via CI artifact/update flow
- Modify: `docs/shots/settings-dark.png` via CI artifact/update flow
- Reference: `.github/workflows/screenshots.yml`, `src-tauri/src/screenshot.rs`

**Interfaces:**
- Produces deterministic `--screenshot-mode popover-light|popover-dark|settings-light|settings-dark` that bypasses monitors/model inference and renders fixed fixture data.

- [ ] **Step 1: Write failing screenshot-mode parsing/state test**

Assert each mode maps to one theme/window fixture and normal launch does not enter screenshot mode.

- [ ] **Step 2: Run red, implement deterministic mode, run green**

Run: `dotnet test tests-dotnet/Butchi.App.Tests --filter Screenshot`
Expected: red then green.

- [ ] **Step 3: Add Windows CI capture**

Build the real Avalonia executable, launch each mode, capture only the target window, upload four PNG artifacts, and fail if any expected image is absent/zero-byte. Do not use mocked HTML/SVG screenshots.

- [ ] **Step 4: Commit**

```bash
git add src-dotnet/Butchi.App .github/workflows/dotnet-screenshots.yml tests-dotnet/Butchi.App.Tests
git commit -m "ci: capture real Avalonia UI screenshots"
```

---

### Task 14: Build old-vs-new benchmark harness and enforce cutover metrics

**Files:**
- Create: `src-dotnet/Butchi.Benchmarks/BenchmarkResult.cs`
- Create: `src-dotnet/Butchi.Benchmarks/JsonBenchmarkWriter.cs`
- Create: `src-dotnet/Butchi.Benchmarks/Program.cs`
- Create: `scripts/compare-performance.ps1`
- Create: `tests-dotnet/Butchi.Core.Tests/PerformanceGateTests.cs`
- Modify: `.github/workflows/dotnet-ci.yml`

**Interfaces:**
- Produces JSON fields: `runtime`, `model`, `backend`, `gpuLayers`, `context`, `startupMs`, `selectionToPopoverMs`, `popoverToDispatchMs`, `firstTokenMs`, `tokensPerSecond`, `idleWorkingSetMb`, `loadedWorkingSetMb`, `vramMb`, `modelLoadMs`, `runNumber`.

- [ ] **Step 1: Write failing gate-calculation tests**

Given five old and five new warm runs, assert medians; tokens/sec >5% slower fails; first-token >5% slower fails unless end-to-end selection-to-first-token improves >=15%; memory >10% fails pending explicit exception; popover/dispatch targets report failures.

- [ ] **Step 2: Run red**

Run: `dotnet test tests-dotnet/Butchi.Core.Tests --filter PerformanceGateTests`
Expected: FAIL.

- [ ] **Step 3: Implement benchmark result writer and comparison script**

`compare-performance.ps1 -Baseline old.json -Candidate new.json` exits non-zero on a blocked gate and prints a concise table plus reasons. It must never silently accept missing metrics.

- [ ] **Step 4: Add instrumentation hooks**

Measure popover show/dispatch in the Avalonia controller and model load/first token/token count in inference. Keep benchmark mode separate from normal logs.

- [ ] **Step 5: Run green and record local/CI artifact format**

Run: `dotnet test tests-dotnet/Butchi.Core.Tests --filter PerformanceGateTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src-dotnet/Butchi.Benchmarks scripts/compare-performance.ps1 tests-dotnet/Butchi.Core.Tests .github/workflows/dotnet-ci.yml
git commit -m "perf: add Tauri versus Avalonia cutover gates"
```

---

### Task 15: Adapt x64/ARM64 release, MSIX, GitHub Release, and Microsoft Store workflows

**Files:**
- Modify: `.github/workflows/release.yml`
- Modify: `.github/workflows/publish-windows-store.yml`
- Create or modify: `packaging/windows/Package.appxmanifest`
- Create or modify: `packaging/windows/Butchi.wapproj` only if required by the chosen MSIX build path
- Modify: `README.md` release-development sections only after package smoke passes

**Interfaces:**
- Produces self-contained `win-x64` and `win-arm64` application packages and Store-compatible MSIX/MSIXBundle while preserving current Store identity/signing variables.

- [ ] **Step 1: Add package validation commands before changing release publication**

CI must run both:

```powershell
dotnet publish src-dotnet/Butchi.App/Butchi.App.csproj -c Release -r win-x64 --self-contained true
dotnet publish src-dotnet/Butchi.App/Butchi.App.csproj -c Release -r win-arm64 --self-contained true
```

and verify `Butchi.exe` plus required LLamaSharp native libraries exist in each publish directory.

- [ ] **Step 2: Adapt release workflow to package .NET output**

Keep existing tag trigger semantics. Produce architecture-labelled archives/installers and GitHub Release assets. Do not remove the old workflow path until the new artifacts pass smoke checks on the branch.

- [ ] **Step 3: Adapt Store workflow preserving identity**

Reuse current Partner Center credentials, package identity, publisher, signing, and version mapping. Only replace the package-build input/output path. Validate generated manifest architecture entries for x64 and ARM64.

- [ ] **Step 4: Run workflow syntax/package smoke validation and commit**

Run local `dotnet publish` commands and any existing repository workflow lint/check. Expected: both architecture outputs complete without missing native dependencies.

```bash
git add .github/workflows/release.yml .github/workflows/publish-windows-store.yml packaging README.md
git commit -m "build: package Avalonia Butchi for Windows releases"
```

---

### Task 16: Run full feature-parity validation on Windows

**Files:**
- Create: `docs/superpowers/validation/2026-08-25-avalonia-parity.md`
- Modify only implementation files for defects found during this validation, always with a failing regression test first.

**Interfaces:**
- Produces signed-off parity evidence required by cutover.

- [ ] **Step 1: Run automated suite**

```powershell
dotnet build Butchi.slnx -c Release
dotnet test Butchi.slnx -c Release --no-build
dotnet publish src-dotnet/Butchi.App/Butchi.App.csproj -c Release -r win-x64 --self-contained true
dotnet publish src-dotnet/Butchi.App/Butchi.App.csproj -c Release -r win-arm64 --self-contained true
```

Expected: all pass.

- [ ] **Step 2: Validate user workflows on Windows x64**

Record pass/fail for: first-run Settings when model missing; download/load default model; mouse selection; Double-Ctrl; UIA capture; clipboard fallback; Translate; Rewrite; both auto-run; Copy; Replace; None; manual input; favorite language rerun; prompt edits/profiles; history search/delete/clear/retention; System/Light/Dark; tray; single instance; clear local AI data; GPU Auto fallback; explicit CPU; explicit GPU error; Escape/auto-hide; popover auto-grow.

- [ ] **Step 3: Validate ARM64 package startup and CPU/Vulkan backend availability on real/appropriate ARM64 environment**

Do not mark this passed from compilation alone. If hosted CI cannot exercise native inference, record the required manual/device validation explicitly as a blocking cutover item.

- [ ] **Step 4: Fix every failure TDD-first and rerun the affected validation**

For each defect: add a regression test, run red, implement, run green, then update the validation document with evidence.

- [ ] **Step 5: Commit parity evidence**

```bash
git add docs/superpowers/validation src-dotnet tests-dotnet
git commit -m "test: validate Avalonia feature parity"
```

---

### Task 17: Run performance comparison and make the cutover decision

**Files:**
- Create: `docs/superpowers/validation/2026-08-25-avalonia-performance.md`
- Add benchmark JSON artifacts under CI/release artifacts, not Git history unless small and intentionally retained.

**Interfaces:**
- Consumes Task 14 benchmark format and Global Constraints.
- Produces explicit `PASS` or `BLOCKED` cutover decision with metric evidence.

- [ ] **Step 1: Benchmark current Tauri build five warm runs**

Use the same reference machine/model/backend/GPU layers/context/prompt/max tokens. Save `tauri.json`.

- [ ] **Step 2: Benchmark Avalonia build five warm runs**

Use identical inputs. Save `avalonia.json`.

- [ ] **Step 3: Compare gates**

Run:

```powershell
./scripts/compare-performance.ps1 -Baseline tauri.json -Candidate avalonia.json
```

Expected: exit 0 only if all non-excepted gates pass.

- [ ] **Step 4: If blocked, diagnose before cutover**

Do not weaken thresholds. Add a failing regression/benchmark test for the identified bottleneck where feasible, fix it, and repeat five-run measurements. Any requested exception must be documented with exact metric delta and user approval.

- [ ] **Step 5: Commit performance report**

```bash
git add docs/superpowers/validation/2026-08-25-avalonia-performance.md
git commit -m "perf: validate Avalonia migration performance"
```

---

### Task 18: Cut over production runtime and remove Tauri only after all gates pass

**Files:**
- Delete after gates pass: `src-tauri/`
- Delete runtime frontend after gates pass: `src/`, `index.html`, `settings.html`, `package.json`, `package-lock.json`, `vite.config.ts`, `tsconfig.json` where present and no longer used by Pages tooling
- Delete/replace obsolete Tauri screenshot/build scripts only after confirming Pages does not depend on them
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/screenshots.yml` or remove in favor of `dotnet-screenshots.yml`
- Modify: `README.md`
- Modify: `PRIVACY.md` only where runtime implementation wording references Tauri/Rust

**Interfaces:**
- Produces the final single production runtime: .NET 10 + Avalonia + LLamaSharp.

- [ ] **Step 1: Verify cutover prerequisites from Tasks 16 and 17**

Require parity report PASS, performance report PASS, x64/ARM64 package outputs, Store package validation, and real Windows selection/replacement validation. If any is missing, STOP; do not delete Tauri.

- [ ] **Step 2: Remove obsolete runtime files and switch canonical CI**

Keep GitHub Pages assets/workflow intact. Canonical CI becomes .NET build/test/package/screenshot. Remove Node/Rust application requirements from README.

- [ ] **Step 3: Run complete verification after deletion**

```powershell
dotnet restore Butchi.slnx
dotnet build Butchi.slnx -c Release
dotnet test Butchi.slnx -c Release --no-build
dotnet publish src-dotnet/Butchi.App/Butchi.App.csproj -c Release -r win-x64 --self-contained true
dotnet publish src-dotnet/Butchi.App/Butchi.App.csproj -c Release -r win-arm64 --self-contained true
```

Also validate workflow YAML and deterministic screenshots.
Expected: PASS with no references to `tauri`, `cargo`, `npm run dev`, or `rs-llama` in runtime build documentation/workflows.

- [ ] **Step 4: Commit cutover**

```bash
git add -A
git commit -m "feat: cut Butchi over to Avalonia and LLamaSharp"
```

- [ ] **Step 5: Request code review before merge**

Use `superpowers:requesting-code-review`; resolve findings with `superpowers:receiving-code-review`; then run `superpowers:verification-before-completion`. Do not merge until required CI/checks are green.

---

## Plan Self-Review

- Spec coverage: architecture, UI, Windows capture/replacement, inference/model lifetime, GPU policy, scheduler, persistence/history, downloads/privacy, error handling, testing, screenshots, x64/ARM64, Store packaging, performance, rollback, and final Tauri deletion all map to explicit tasks.
- Scope: this is large but sequential rather than independent; each task leaves a testable parallel .NET runtime while preserving the existing Tauri baseline. Splitting inference/UI/platform into separate specs would add coordination overhead without independent shippable products, so one plan is retained.
- Placeholder scan: no implementation step relies on `TBD`, `TODO`, or unspecified “handle errors/tests” instructions.
- Type consistency: `IInferenceEngine`, `AppConfig`, `SelectionSnapshot`, `IHistoryStore`, `InferenceScheduler`, and ViewModel state names are introduced before consumers.
- Safety: old runtime deletion is isolated to Task 18 and explicitly blocked by parity/performance/package evidence.
