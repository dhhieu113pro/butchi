# Popover Inference Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the production popover's Translate and Rewrite actions to the loaded local LLamaSharp model with streamed output, retry/rerun, language persistence, and Windows copy/replace result actions.

**Architecture:** Keep `TextActionScheduler` as the sole owner of prompt construction, inference serialization, run IDs, cancellation, stale-run suppression, and automatic result actions. Add scheduler run callbacks plus a focused `PopoverActionController`; compose it with a production `WindowsResultActionSink` in startup while keeping screenshot fixtures inference-free.

**Tech Stack:** .NET 10, C#, Avalonia, LLamaSharp, xUnit, Windows clipboard/keyboard APIs, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-08-28-popover-inference-wiring-design.md`

## Global Constraints

- Do not call LLamaSharp directly from `PopoverWindow` or `PopoverViewModel`.
- `TextActionScheduler` remains the only authority that creates inference run IDs.
- Every run reloads `AppConfig`; the current popover target language overrides saved language for that Translate run.
- Changing language persists it; it reruns only when Translate is selected.
- Screenshot fixtures must not create or start production inference orchestration.
- Replace snapshots clipboard text, pastes generated text, and restores/clears the clipboard in `finally` after mutation.
- Follow strict RED → GREEN TDD for every production change.

---

### Task 1: Scheduler-owned streaming callbacks

**Files:**
- Create: `src/Butchi.Core/Actions/TextActionRunCallbacks.cs`
- Modify: `src/Butchi.Core/Actions/TextActionScheduler.cs`
- Modify: `tests/Butchi.Core.Tests/TextActionSchedulerTests.cs`

**Interfaces:**
- Produces: `public sealed record TextActionRunCallbacks(Action<long>? Started = null, Action<long, string>? Chunk = null);`
- Produces: `TextActionScheduler.RunAsync(..., CancellationToken cancellationToken, TextActionRunCallbacks? callbacks = null)` while preserving existing callers.

- [ ] **Step 1: Write failing scheduler callback tests**

Add tests proving `Started` receives a nonzero scheduler run ID, every `Chunk` receives that same ID in generation order, and an obsolete run emits no chunks after replacement. Use the existing fake inference engine pattern in `TextActionSchedulerTests` and assert a callback sequence equivalent to `started:1`, `chunk:1:a`, `chunk:1:b`.

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/Butchi.Core.Tests/Butchi.Core.Tests.csproj --filter FullyQualifiedName~TextActionSchedulerTests`

Expected: compile/test failure because `TextActionRunCallbacks` and the callback parameter do not exist.

- [ ] **Step 3: Implement the callback contract minimally**

Create:

```csharp
namespace Butchi.Core.Actions;

public sealed record TextActionRunCallbacks(
    Action<long>? Started = null,
    Action<long, string>? Chunk = null);
```

Extend `RunAsync` with `TextActionRunCallbacks? callbacks = null`. After the inference lane is acquired and `IsObsolete(action, runId)` is false, invoke `callbacks?.Started?.Invoke(runId)`. Inside generation, after the current-run check and before accumulating output, invoke `callbacks?.Chunk?.Invoke(runId, chunk)`. Never invoke callbacks after an obsolete check fails.

- [ ] **Step 4: Run scheduler tests and verify GREEN**

Run the focused command from Step 2, then `dotnet test tests/Butchi.Core.Tests/Butchi.Core.Tests.csproj`.

Expected: PASS; existing scheduler behavior without callbacks remains green.

- [ ] **Step 5: Commit**

```bash
git add src/Butchi.Core/Actions/TextActionRunCallbacks.cs src/Butchi.Core/Actions/TextActionScheduler.cs tests/Butchi.Core.Tests/TextActionSchedulerTests.cs
git commit -m "feat: stream text action scheduler progress"
```

---

### Task 2: Popover action request events

**Files:**
- Modify: `src/Butchi.App/Popover/PopoverViewModel.cs`
- Modify: `tests/Butchi.App.Tests/PopoverViewModelTests.cs`

**Interfaces:**
- Produces: `event EventHandler<TextAction>? ActionRequested`
- Existing: `TranslateLanguageRequested`, `RerunRequested`, `CopyRequested`, `ReplaceRequested`

- [ ] **Step 1: Write failing view-model event tests**

Add tests asserting `SelectAction(TextAction.Translate)` and `SelectAction(TextAction.Rewrite)` update `SelectedAction` and raise `ActionRequested` with exactly that action. Keep existing screenshot/session behavior unchanged.

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter FullyQualifiedName~PopoverViewModelTests`

Expected: compile failure because `ActionRequested` does not exist.

- [ ] **Step 3: Add the event and raise it from `SelectAction`**

Add:

```csharp
public event EventHandler<TextAction>? ActionRequested;
```

At the end of `SelectAction`, after property notifications:

```csharp
ActionRequested?.Invoke(this, action);
```

Do not start inference from `SetSession`; showing selected text remains passive.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the command from Step 2.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Butchi.App/Popover/PopoverViewModel.cs tests/Butchi.App.Tests/PopoverViewModelTests.cs
git commit -m "feat: expose popover action requests"
```

---

### Task 3: Popover inference controller

**Files:**
- Create: `src/Butchi.App/Popover/PopoverActionController.cs`
- Create: `tests/Butchi.App.Tests/PopoverActionControllerTests.cs`

**Interfaces:**
- Consumes: `PopoverViewModel`, `TextActionScheduler`, `IAppConfigStore`, `IResultActionSink`
- Produces: `public sealed class PopoverActionController : IAsyncDisposable`

- [ ] **Step 1: Write failing controller tests for Translate/Rewrite and streaming**

Create tests with a fake `IInferenceEngine`, fake config store, and fake result sink. Construct a real `TextActionScheduler`, then controller. Set a session, call `viewModel.SelectAction(TextAction.Translate)`, and await a test completion signal. Assert the engine receives a prompt containing the source text and configured target language, streamed chunks appear in `viewModel.Translate.Output`, and `IsRunning` becomes false. Repeat for Rewrite and assert Rewrite prompt semantics.

- [ ] **Step 2: Run focused controller tests and verify RED**

Run: `dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter FullyQualifiedName~PopoverActionControllerTests`

Expected: compile failure because `PopoverActionController` does not exist.

- [ ] **Step 3: Implement minimal controller run orchestration**

Implement constructor subscriptions to `ActionRequested`, `RerunRequested`, and `TranslateLanguageRequested`. `RunAsync(TextAction action)` must reject blank `SourceText`, load config with `_configStore.LoadAsync`, overlay target language using `config with { TargetLanguage = AppConfig.NormalizeTargetLanguage(_viewModel.TargetLanguage ?? config.TargetLanguage) }`, and call scheduler with `InputOrigin.Selection` plus callbacks:

```csharp
new TextActionRunCallbacks(
    Started: runId => Dispatch(() => {
        _latestRunIds[action] = runId;
        _viewModel.Begin(action, runId);
    }),
    Chunk: (runId, chunk) => Dispatch(() => {
        if (_viewModel.Append(action, runId, chunk))
            _viewModel.FlushPendingUpdates();
    }))
```

On non-obsolete success dispatch `Complete(action, result.RunId)`. On inference failure, use the latest scheduler-published run ID and dispatch `Fail`. Treat cancellation from disposal/obsolete work as non-error. `Dispatch` must marshal to Avalonia `Dispatcher.UIThread` in production while allowing deterministic execution when already on the UI thread.

- [ ] **Step 4: Add failing tests for rerun, language persistence, stale runs, empty input, and failure**

Assert: `RequestRerun()` invokes the selected action again; `RequestFavoriteLanguage("Japanese")` saves `config with { TargetLanguage = "Japanese" }` and reruns only when Translate is selected; Rewrite selection persists language without starting Rewrite; a superseded Translate cannot overwrite the newer output; blank source never invokes engine; engine exceptions create a retryable error state.

- [ ] **Step 5: Implement the remaining controller behavior**

Persist language with `await _configStore.SaveAsync(config with { TargetLanguage = normalized }, token)`. Keep one lifetime `CancellationTokenSource`, one latest scheduler run ID per action, and tracked background tasks so `DisposeAsync` cancels and awaits outstanding event-triggered operations. Do not create inference run IDs in the controller.

- [ ] **Step 6: Run controller tests and verify GREEN**

Run the focused command from Step 2, then the full `Butchi.App.Tests` project.

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Butchi.App/Popover/PopoverActionController.cs tests/Butchi.App.Tests/PopoverActionControllerTests.cs
git commit -m "feat: orchestrate popover inference actions"
```

---

### Task 4: Explicit Copy/Replace controller actions

**Files:**
- Modify: `src/Butchi.App/Popover/PopoverActionController.cs`
- Modify: `tests/Butchi.App.Tests/PopoverActionControllerTests.cs`

**Interfaces:**
- Consumes existing `CopyRequested` and `ReplaceRequested` view-model events.

- [ ] **Step 1: Write failing explicit-result tests**

Complete a generated result, call `RequestCopy()` and `RequestReplace()`, and assert the fake `IResultActionSink` receives exactly the completed output. Add sink-throws tests asserting generated output remains intact and the selected state exposes a concise action error rather than clearing output.

- [ ] **Step 2: Run focused tests and verify RED**

Run the Task 3 focused command.

Expected: FAIL because controller does not subscribe to Copy/Replace.

- [ ] **Step 3: Wire Copy/Replace through the sink**

Subscribe to `CopyRequested` and `ReplaceRequested`; launch lifetime-bound async operations calling `_resultSink.CopyAsync(text, token)` or `_resultSink.ReplaceAsync(text, token)`. On failure preserve `SelectedState.Output` and expose the error using the matching current presentation run ID.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 3 focused command.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Butchi.App/Popover/PopoverActionController.cs tests/Butchi.App.Tests/PopoverActionControllerTests.cs
git commit -m "feat: wire popover result actions"
```

---

### Task 5: Windows result action sink

**Files:**
- Create: `src/Butchi.Platform.Windows/Actions/IWindowsResultClipboard.cs`
- Create: `src/Butchi.Platform.Windows/Actions/IWindowsPasteSender.cs`
- Create: `src/Butchi.Platform.Windows/Actions/WindowsResultActionSink.cs`
- Create: `src/Butchi.Platform.Windows/Actions/WindowsResultClipboard.cs`
- Create: `src/Butchi.Platform.Windows/Actions/WindowsPasteSender.cs`
- Create: `tests/Butchi.Platform.Windows.Tests/WindowsResultActionSinkTests.cs`

**Interfaces:**
- `IWindowsResultClipboard.GetTextAsync`, `SetTextAsync`, `ClearAsync`
- `IWindowsPasteSender.SendPasteAsync`
- `WindowsResultActionSink : IResultActionSink`

- [ ] **Step 1: Write failing sink unit tests against abstractions**

Test Copy writes expected text. Test Replace call order: read prior text → set generated text → send paste → restore prior text. Test no prior text ends with `ClearAsync`. Test paste exception and cancellation after mutation still restore/clear in `finally`.

- [ ] **Step 2: Run focused platform tests and verify RED**

Run: `dotnet test tests/Butchi.Platform.Windows.Tests/Butchi.Platform.Windows.Tests.csproj --filter FullyQualifiedName~WindowsResultActionSinkTests`

Expected: compile failure because the sink/abstractions do not exist.

- [ ] **Step 3: Implement `WindowsResultActionSink` minimally**

Use this control flow:

```csharp
public async Task ReplaceAsync(string text, CancellationToken token)
{
    token.ThrowIfCancellationRequested();
    var previous = await _clipboard.GetTextAsync(token);
    var mutated = false;
    try
    {
        await _clipboard.SetTextAsync(text, token);
        mutated = true;
        token.ThrowIfCancellationRequested();
        await _paste.SendPasteAsync(token);
        await Task.Delay(_pasteConsumptionDelay, CancellationToken.None);
    }
    finally
    {
        if (mutated)
        {
            if (previous is null) await _clipboard.ClearAsync(CancellationToken.None);
            else await _clipboard.SetTextAsync(previous, CancellationToken.None);
        }
    }
}
```

`CopyAsync` only calls `SetTextAsync(text, token)`.

- [ ] **Step 4: Implement production Windows adapters**

Follow existing Windows clipboard/keyboard P/Invoke patterns in `Butchi.Platform.Windows`. `WindowsResultClipboard` must marshal clipboard access according to the existing platform approach; `WindowsPasteSender` emits Ctrl+V key down/up safely. Keep OS details out of `WindowsResultActionSink`.

- [ ] **Step 5: Run platform tests and verify GREEN**

Run the focused command from Step 2, then full `Butchi.Platform.Windows.Tests`.

Expected: PASS without requiring an interactive desktop for sink unit tests.

- [ ] **Step 6: Commit**

```bash
git add src/Butchi.Platform.Windows/Actions tests/Butchi.Platform.Windows.Tests/WindowsResultActionSinkTests.cs
git commit -m "feat: add Windows result action sink"
```

---

### Task 6: Production composition and lifetime

**Files:**
- Modify: `src/Butchi.App/Startup/ButchiRuntimeFactory.cs`
- Modify: `src/Butchi.App/Startup/ButchiRuntime.cs`
- Modify: `src/Butchi.App/Popover/PopoverWindow.cs`
- Modify: `tests/Butchi.App.Tests/StartupRuntimeContractTests.cs` or the existing startup composition contract test file.

**Interfaces:**
- Consumes: `WindowsResultActionSink`, `TextActionScheduler`, `PopoverActionController`
- `ButchiRuntime` owns/disposes controller and scheduler exactly once.

- [ ] **Step 1: Write failing production composition contract tests**

Assert production `CreateAsync` constructs `WindowsResultActionSink`, constructs `TextActionScheduler` with `services.InferenceEngine`, constructs `PopoverActionController`, initializes the popover target language from the supplied/current config, and gives runtime ownership of controller/scheduler. Assert `CreatePopoverScreenshot` remains fixture-only and contains no production controller/scheduler construction.

- [ ] **Step 2: Run focused app tests and verify RED**

Run: `dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter FullyQualifiedName~StartupRuntime`

Expected: FAIL because production composition does not yet wire these components.

- [ ] **Step 3: Wire production runtime**

In `CreateAsync`, create the sink, scheduler, view model, and controller before the window. Initialize the session/default language from `config.TargetLanguage`. Pass controller/scheduler into `ButchiRuntime` as owned `IAsyncDisposable` dependencies. Keep screenshot factory construction unchanged apart from signatures required by compilation.

- [ ] **Step 4: Dispose inference orchestration during runtime shutdown**

Change `ButchiRuntime.DisposeAsync` to async, cancel interaction first, destroy UI, then `await controller.DisposeAsync()` and `await scheduler.DisposeAsync()` exactly once. Preserve current idempotency guard.

- [ ] **Step 5: Run focused and full app tests and verify GREEN**

Run the focused command from Step 2, then `dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj`.

Expected: PASS; screenshot tests remain deterministic.

- [ ] **Step 6: Commit**

```bash
git add src/Butchi.App/Startup/ButchiRuntimeFactory.cs src/Butchi.App/Startup/ButchiRuntime.cs src/Butchi.App/Popover/PopoverWindow.cs tests/Butchi.App.Tests
git commit -m "fix: wire popover to local inference"
```

---

### Task 7: Full regression and release verification

**Files:**
- Modify only if verification exposes a regression; any fix must begin with a failing regression test in the owning test project.

- [ ] **Step 1: Run the complete solution tests**

Run: `dotnet test Butchi.slnx --configuration Release`

Expected: all tests PASS.

- [ ] **Step 2: Run release build**

Run: `dotnet build Butchi.slnx --configuration Release --no-restore`

Expected: exit 0 with no new warnings attributable to this change.

- [ ] **Step 3: Push branch and verify GitHub Actions**

Push `fix/popover-inference-wiring`, then verify CI, Release, Release Readiness, screenshot-smoke, Windows x64, and Windows ARM64 jobs for the exact head commit.

Expected: all required checks completed successfully.

- [ ] **Step 4: Open PR only after exact-head GREEN**

Use title `fix: wire popover actions to local inference`. Summarize the root cause, scheduler/controller wiring, Windows result sink, TDD coverage, and screenshot isolation. Do not merge without explicit authorization.
