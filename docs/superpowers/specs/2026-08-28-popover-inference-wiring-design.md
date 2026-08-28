# Popover Inference Wiring Design

## Problem

Butchi creates the production `LLamaSharpInferenceEngine` and `FileModelManager`, but the production popover is created with only `PopoverViewModel`. The Translate and Rewrite buttons only update `SelectedAction`; no production component invokes `TextActionScheduler` or `IInferenceEngine`. As a result, the UI can display selected text and action state, but no LLM request is started.

The existing `TextActionScheduler` already owns prompt construction, per-action cancellation, serialized inference, stale-run protection, and configured automatic result application. The fix connects the popover to this existing scheduler rather than duplicating inference orchestration in the Avalonia view.

## Goals

- Clicking **Translate** or **Rewrite** starts local inference immediately for the current selected text.
- Translate uses the current target language from configuration/UI.
- Changing the target language while Translate is active immediately reruns Translate with the new language.
- **Run again** reruns the currently selected action.
- Generated output is surfaced incrementally in the popover.
- Failures are visible and retryable without closing the popover.
- Existing automatic result behavior (`None`, `Copy`, `Replace`) continues to be applied by `TextActionScheduler`.
- The production runtime wires a real Windows implementation of `IResultActionSink`.
- Existing screenshot fixtures remain deterministic and do not invoke live inference.

## Non-goals

- Replacing `TextActionScheduler` or moving prompt construction into the UI.
- Adding new model formats, providers, or remote inference.
- Redesigning the popover UI.
- Changing model download/load policy introduced by PR #34.

## Architecture

### PopoverActionController

Add a focused application-layer controller between `PopoverViewModel` and `TextActionScheduler`.

Responsibilities:

- subscribe to popover action events;
- load the latest `AppConfig` before each run;
- apply the popover's current target language to the run configuration without mutating unrelated settings;
- start Translate/Rewrite runs through `TextActionScheduler`;
- map scheduler run callbacks into `PopoverViewModel.Begin`, `Append`, `Complete`, and `Fail`;
- rerun the selected action when requested;
- rerun Translate when the target language changes while Translate is selected;
- persist a newly selected target language;
- forward explicit Copy/Replace button actions through an `IResultActionSink`;
- own cancellation/disposal for popover-triggered work.

The controller contains orchestration only. It does not construct prompts or call LLamaSharp directly.

### TextActionScheduler run callbacks

Extend `TextActionScheduler.RunAsync` with an optional callback contract that reports scheduler-owned run identity and streamed chunks. Use a small immutable callback holder such as:

```csharp
public sealed record TextActionRunCallbacks(
    Action<long>? Started = null,
    Action<long, string>? Chunk = null);
```

The scheduler invokes `Started(runId)` after the run becomes current and acquires the inference lane, immediately before prompt generation begins. For each generated chunk, after confirming the run is still current, it invokes `Chunk(runId, chunk)` and appends the same chunk to its accumulated output.

This keeps `TextActionScheduler` as the only authority that creates run IDs. `PopoverActionController` uses `Started` to call `ViewModel.Begin(action, runId)` and `Chunk` to call `ViewModel.Append(action, runId, chunk)`. Existing callers omit the callbacks and retain current behavior.

Callbacks do not change scheduler ownership of cancellation, serialization, stale-run suppression, final output, or automatic result actions.

### WindowsResultActionSink

Add a production Windows implementation of `IResultActionSink`, backed by testable clipboard and keyboard-paste abstractions.

- `CopyAsync` writes the generated text to the Windows clipboard.
- `ReplaceAsync` snapshots the current text clipboard content, writes the generated output, sends a Ctrl+V paste into the active application, waits only long enough for the paste command to consume the clipboard, then restores the original text clipboard content. If no text clipboard content existed, it clears the temporary replacement text after paste instead of inventing prior content.
- Cancellation is checked before clipboard mutation and before paste dispatch. Restoration runs in `finally` once clipboard mutation has occurred.

The implementation stays in the Windows platform layer and is injected into the scheduler/controller from startup composition. Unit tests use the abstractions rather than a live desktop.

## Production Composition

`StartupApplicationServices` continues to own the singleton `LLamaSharpInferenceEngine`, model manager, config store, and history store.

`ButchiRuntimeFactory.CreateAsync` creates:

1. a production `WindowsResultActionSink`;
2. a `TextActionScheduler` using `services.InferenceEngine` and that sink;
3. a `PopoverViewModel` initialized from the current configuration target language;
4. a `PopoverActionController` using the view model, scheduler, config store, and sink;
5. a `PopoverWindow` using the view model and retaining the controller for the lifetime of the window/runtime.

The runtime disposes the controller/scheduler during application shutdown so active generation is cancelled cleanly.

Screenshot creation remains isolated: screenshot popovers use fixture view models only and do not construct or start the production action controller.

## Interaction Flow

### Selection activation

1. Windows selection activation obtains the selected text.
2. `PopoverWindow.SetSelectionInput` calls `PopoverViewModel.SetSession` with the selected text while preserving the current configured target language.
3. The popover is shown. No inference starts merely from showing selected text.

### Translate / Rewrite

1. User clicks Translate or Rewrite.
2. `PopoverViewModel.SelectAction` updates the selected action and raises an action-request event.
3. `PopoverActionController` receives the request.
4. Controller loads the latest `AppConfig`.
5. For Translate, controller overlays the current popover target language onto the run configuration.
6. Controller calls `TextActionScheduler.RunAsync` with `InputOrigin.Selection` and run callbacks.
7. Scheduler invokes `Started(runId)`; controller calls `Begin(action, runId)` on the UI thread.
8. Scheduler invokes `Chunk(runId, chunk)` for each current-run chunk; controller calls `Append` and flushes pending UI updates on the UI thread.
9. Successful non-obsolete completion calls `Complete(action, result.RunId)`.
10. Failure after a run has started calls `Fail(action, activeRunId, conciseMessage)`.

If configuration loading fails before the scheduler publishes a run ID, the controller displays a controller-level error for the selected action by starting a local presentation run ID that is explicitly outside scheduler correlation and is never used for streamed output. This is the only pre-scheduler error path; all actual inference run IDs come from the scheduler.

### Language change

When Translate is selected and the user chooses Vietnamese, English, or Japanese, `TargetLanguage` is updated, persisted through the config store, and the controller immediately starts a new Translate run. Scheduler stale-run handling cancels/suppresses the prior Translate run.

If Rewrite is selected, changing the stored Translate target language does not start Rewrite.

### Run again

The controller reruns `SelectedAction` against the current `SourceText` and target language.

### Copy / Replace buttons

Explicit popover Copy/Replace buttons call the production `IResultActionSink` directly for the current completed output. These explicit actions are independent from configured automatic result actions, which remain the scheduler's responsibility.

## Error Handling

- Empty source text: do not invoke inference; show an actionable error in the selected action state.
- Model not loaded: surface the inference engine/model readiness error as `Local model is not loaded. Open Model settings to continue.` without crashing the popover.
- Inference exception: fail the current scheduler run and keep **Run again** available.
- Cancellation caused by a newer run: do not show an error for the obsolete run.
- App shutdown/window disposal: cancel active controller work and dispose the scheduler.
- Explicit copy/replace failures: show a concise error without discarding the completed generated output.
- Automatic result failures continue to propagate through the scheduler run.

## Concurrency

`TextActionScheduler` remains the authority for run IDs, same-action cancellation, stale-run detection, and the single inference lane. The controller does not maintain a competing inference run-ID system.

The controller stores only the latest scheduler-published run ID per action so it can correlate completion/failure with the `PopoverViewModel` guards. Its lifetime `CancellationTokenSource` exists only to stop work during disposal.

The scheduler invokes callbacks only after verifying the run is current. The view model's existing run-ID checks remain the final UI protection against stale updates.

## Configuration

Every run reads the latest saved `AppConfig` so changes to temperature, max tokens, prompts, result action, and target language are honored without restarting Butchi.

For a Translate run triggered from the popover, the current popover target language takes precedence over the saved target language for that run. Selecting a language persists it through the existing config store so later sessions use it by default.

## Testing Strategy

### Core scheduler tests

- `Started` receives the scheduler-created run ID;
- `Chunk` receives the same run ID and generated chunks in order;
- stale/obsolete runs do not emit further chunks;
- existing final-output and automatic-result behavior remains unchanged when callbacks are omitted.

### App/controller tests

- clicking Translate invokes scheduler with current source text and Translate action;
- clicking Rewrite invokes Rewrite;
- current config is loaded for each run;
- target language is applied to Translate;
- language change while Translate is selected persists the language and triggers a new Translate run;
- language change while Rewrite is selected persists the language but does not trigger Rewrite;
- Run again reruns the current action;
- scheduler `Started`/`Chunk` callbacks update the matching presentation run;
- successful generation completes the matching state;
- inference errors fail the matching state and remain retryable;
- obsolete/cancelled runs cannot overwrite the latest run;
- empty source text does not invoke the scheduler;
- explicit Copy/Replace events call the result sink;
- explicit Copy/Replace failures preserve completed output.

### Windows platform tests

- copy writes expected text;
- replace snapshots clipboard text, writes result, sends Ctrl+V, and restores the snapshot;
- replace clears temporary clipboard text when no prior text existed;
- restoration occurs after paste failures/cancellation once mutation has begun;
- tests run through clipboard/paste abstractions without requiring a live desktop.

### Composition/contract tests

- production runtime constructs `TextActionScheduler` and `PopoverActionController` with the real inference engine;
- production runtime supplies `WindowsResultActionSink`;
- screenshot popovers do not construct/start live inference orchestration;
- controller/scheduler are disposed with the runtime.

## Acceptance Criteria

A production Windows build satisfies the change when selecting text, opening the popover, and clicking Translate or Rewrite produces streamed local-LLM output using the currently loaded selected model. Changing Translate language persists the language and reruns translation immediately, Run again works, errors are visible/retryable, configured automatic result actions still work, explicit Copy/Replace actions work, and screenshot/CI paths remain deterministic.
