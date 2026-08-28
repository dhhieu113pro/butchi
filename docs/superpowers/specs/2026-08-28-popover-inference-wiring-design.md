# Popover Inference Wiring Design

## Problem

Butchi creates the production `LLamaSharpInferenceEngine` and `FileModelManager`, but the production popover is created with only `PopoverViewModel`. The Translate and Rewrite buttons only update `SelectedAction`; no production component invokes `TextActionScheduler` or `IInferenceEngine`. As a result, the UI can display selected text and action state, but no LLM request is started.

The existing `TextActionScheduler` already owns prompt construction, per-action cancellation, serialized inference, stale-run protection, and configured automatic result application. The fix should connect the popover to this existing scheduler rather than duplicate inference orchestration in the Avalonia view.

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
- map scheduler progress/results into `PopoverViewModel.Begin`, `Append`, `Complete`, and `Fail`;
- rerun the selected action when requested;
- rerun Translate when the target language changes while Translate is selected;
- forward explicit Copy/Replace button actions through an `IResultActionSink`;
- own cancellation/disposal for popover-triggered work.

The controller contains orchestration only. It does not construct prompts or call LLamaSharp directly.

### TextActionScheduler progress

Extend `TextActionScheduler.RunAsync` with an optional progress callback, for example `IProgress<string>?` or `Action<string>?`, invoked for each generated chunk after stale-run checks and before/after appending to the scheduler's accumulated output.

The callback must not change scheduler ownership of cancellation, serialization, stale-run suppression, final output, or automatic result actions. Existing callers can omit the callback and retain current behavior.

### WindowsResultActionSink

Add a production Windows implementation of `IResultActionSink`.

- `CopyAsync` writes the generated text to the Windows clipboard.
- `ReplaceAsync` replaces the user's current selection using the existing clipboard/keyboard integration pattern. It must preserve the user's clipboard contents where practical and restore them after the replacement operation.

The implementation stays in the Windows platform layer and is injected into the scheduler/controller from startup composition.

## Production Composition

`StartupApplicationServices` continues to own the singleton `LLamaSharpInferenceEngine`, model manager, config store, and history store.

`ButchiRuntimeFactory.CreateAsync` will create:

1. a production `WindowsResultActionSink`;
2. a `TextActionScheduler` using `services.InferenceEngine` and that sink;
3. a `PopoverViewModel`;
4. a `PopoverActionController` using the view model, scheduler, config store, and sink;
5. a `PopoverWindow` using the view model and retaining the controller for the lifetime of the window/runtime.

The runtime must dispose the controller/scheduler during application shutdown so active generation is cancelled cleanly.

Screenshot creation remains isolated: screenshot popovers use fixture view models only and do not construct or start the production action controller.

## Interaction Flow

### Selection activation

1. Windows selection activation obtains the selected text.
2. `PopoverWindow.SetSelectionInput` calls `PopoverViewModel.SetSession` with the selected text.
3. The popover is shown. No inference starts merely from showing selected text.

### Translate / Rewrite

1. User clicks Translate or Rewrite.
2. `PopoverViewModel.SelectAction` updates the selected action and raises an action-request event.
3. `PopoverActionController` receives the request.
4. Controller loads the latest `AppConfig`.
5. For Translate, controller overlays the current target language onto the run configuration.
6. Controller calls `TextActionScheduler.RunAsync` with `InputOrigin.Selection` and a progress callback.
7. Controller calls `Begin` before awaiting generation.
8. Each scheduler progress chunk is forwarded to `Append` and flushed to the UI.
9. Successful non-obsolete completion calls `Complete`.
10. Failure calls `Fail` with a concise actionable message.

### Language change

When Translate is selected and the user chooses Vietnamese, English, or Japanese, `TargetLanguage` is updated and the controller immediately starts a new Translate run. Scheduler stale-run handling cancels/suppresses the prior Translate run.

If Rewrite is selected, changing the stored Translate target language does not start Rewrite.

### Run again

The controller reruns `SelectedAction` against the current `SourceText` and target language.

### Copy / Replace buttons

Explicit popover Copy/Replace buttons call the production `IResultActionSink` directly for the current completed output. These explicit actions are independent from configured automatic result actions, which remain the scheduler's responsibility.

## Error Handling

- Empty source text: do not invoke inference; show an actionable error in the selected action state.
- Model not loaded: surface the inference engine/model readiness error as a user-facing message such as "Local model is not loaded. Open Model settings to continue." without crashing the popover.
- Inference exception: fail the current run and keep **Run again** available.
- Cancellation caused by a newer run: do not show an error for the obsolete run.
- App shutdown/window disposal: cancel active controller work and dispose the scheduler.
- Copy/replace failures: surface a concise error for explicit user actions; automatic result failures propagate through the scheduler run as they do today.

## Concurrency

`TextActionScheduler` remains the authority for run IDs, same-action cancellation, stale-run detection, and the single inference lane. The controller must not maintain a second competing run-ID system. It may keep only a disposal/lifetime cancellation token and use the `TextActionRunResult.RunId` returned by the scheduler for final UI state correlation if needed.

Progress callbacks for obsolete runs must not update the visible state. The scheduler should only invoke progress after verifying the run is still current, and the view model's existing run-ID guards remain the final UI protection.

## Configuration

Every run reads the latest saved `AppConfig` so changes to temperature, max tokens, prompts, result action, and target language are honored without restarting Butchi.

For a Translate run triggered from the popover, the current popover target language takes precedence over the saved target language for that run. Selecting a favorite language should persist the new target language through the existing config store so later sessions use it by default.

## Testing Strategy

### Core scheduler tests

- progress callback receives generated chunks in order;
- stale/obsolete runs do not emit further progress;
- existing final-output and automatic-result behavior remains unchanged when no progress callback is supplied.

### App/controller tests

- clicking Translate invokes scheduler with current source text and Translate action;
- clicking Rewrite invokes Rewrite;
- current config is loaded for each run;
- target language is applied to Translate;
- language change while Translate is selected triggers a new Translate run;
- language change while Rewrite is selected does not trigger Rewrite;
- Run again reruns the current action;
- streamed chunks update the selected presentation state;
- successful generation completes the state;
- inference errors fail the state and remain retryable;
- obsolete/cancelled runs cannot overwrite the latest run;
- empty source text does not invoke the scheduler;
- explicit Copy/Replace events call the result sink.

### Windows platform tests

- copy writes expected text;
- replace uses the platform replacement path and handles cancellation;
- clipboard preservation/restoration behavior is verified through platform abstractions where feasible without requiring a live desktop in unit tests.

### Composition/contract tests

- production runtime constructs `TextActionScheduler` and `PopoverActionController` with the real inference engine;
- screenshot popovers do not construct/start live inference orchestration;
- controller/scheduler are disposed with the runtime.

## Acceptance Criteria

A production Windows build satisfies the change when selecting text, opening the popover, and clicking Translate or Rewrite produces streamed local-LLM output using the currently loaded selected model. Changing Translate language reruns translation immediately, Run again works, errors are visible/retryable, configured automatic result actions still work, explicit Copy/Replace actions work, and screenshot/CI paths remain deterministic.
