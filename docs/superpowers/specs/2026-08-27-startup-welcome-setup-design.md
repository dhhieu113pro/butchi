# Startup Welcome Setup Design

## Goal

Make Butchi responsive and visible during startup without blocking Avalonia's UI thread. Normal ready startup remains tray-only. Missing or invalid settings, a missing configured model, or a model-load failure opens one Welcome Setup screen that guides the user to a fully ready state.

## Current problem

`App.OnFrameworkInitializationCompleted` currently performs directory creation, configuration reads, model composition, history reads, view-model creation, window creation, theme application, and tray creation. It synchronously waits on asynchronous operations with `GetAwaiter().GetResult()` while running on Avalonia's UI thread.

When `%APPDATA%\butchi\config.json` exists, `JsonSerializer.DeserializeAsync` yields and its continuation attempts to resume on the blocked UI thread. Startup deadlocks before a management window or tray icon becomes visible. The same blocking pattern is repeated for prompts, models, history, and inference-engine disposal.

The installed-package release probe does not expose this failure because its `trayReady` result checks that tray commands exist rather than creating the interactive startup path.

## Product behavior

### Ready startup

A startup is ready when all of the following are true:

- the configuration file exists and contains valid `AppConfig` JSON;
- the configured catalog model file exists locally;
- the configured model loads successfully into the single shared inference engine;
- the normal runtime composition can be created.

When ready, Butchi creates its popover, management window, trigger services when wired, and tray icon. It does not show a normal window.

### Setup-required startup

Butchi opens one dedicated Welcome Setup window when any of these conditions applies:

- configuration is missing;
- configuration is invalid or unreadable;
- the configured model is absent;
- loading the configured model fails.

The window is a single screen, not a wizard. It contains:

- an explanation that Butchi runs locally;
- a settings section with theme, target language, and default action values initialized from valid configuration or `AppConfig.Default`;
- a model section with catalog selection, local/download state, download progress, and load state;
- a concise, actionable error message and Retry when an operation fails;
- one primary `Finish setup` action;
- an explicit `Exit` action.

Setup is complete only after valid settings have been saved and the selected model has loaded successfully. There is no Skip path. Closing Welcome Setup before completion exits the application so Butchi cannot become an invisible, unusable background process.

After completion, the coordinator creates the normal runtime and tray, then closes Welcome Setup without restarting the process.

### Non-blocking operational failures

History search or retention failures do not make startup setup-required. They remain page-level errors in the existing History UI. Network failure while downloading a model and inference failure while loading it remain visible in Welcome Setup and are retryable.

## Architecture

### `App`

`App` remains the Avalonia lifecycle owner. `OnFrameworkInitializationCompleted` performs only synchronous framework work:

1. initialize application theme resources;
2. set `ShutdownMode.OnExplicitShutdown`;
3. call the Avalonia base implementation;
4. start one asynchronous startup coordinator without blocking the UI thread.

The asynchronous entry point catches every non-cancellation exception and routes it to visible setup failure UI. `App` stores only the coordinator/startup task and delegates asynchronous cleanup to the runtime owner during shutdown.

### Configuration status

`JsonConfigStore` gains a status-bearing load operation while preserving its existing compatibility API:

```csharp
public enum ConfigLoadState { Ready, Missing, Invalid, Unavailable }

public sealed record ConfigLoadResult(
    AppConfig Config,
    ConfigLoadState State,
    string? ErrorCode = null);

public Task<ConfigLoadResult> LoadWithStatusAsync(
    CancellationToken cancellationToken = default);
```

`LoadAsync` delegates to `LoadWithStatusAsync` and returns `result.Config`, retaining the current fallback-to-default behavior for existing consumers. Startup uses the state-bearing operation so missing, malformed, and inaccessible configuration are not silently mistaken for readiness. Error codes contain exception type/category only and never configuration content.

### Runtime ownership

`ButchiRuntime` owns the long-lived resources currently stored directly in `App`: `HttpClient`, the single `LLamaSharpInferenceEngine`, model manager, history store, popover, management window, tray icon collection, and any Windows trigger services. It implements `IAsyncDisposable` and disposes resources in a deterministic order.

`ButchiRuntimeFactory.CreateAsync(AppConfig, CancellationToken)` composes view models asynchronously. UI objects are created after awaits resume on Avalonia's UI context. It never synchronously waits on tasks.

The runtime exposes:

```csharp
public interface IButchiRuntime : IAsyncDisposable
{
    void StartTray();
}

public interface IButchiRuntimeFactory
{
    ValueTask<IButchiRuntime> CreateAsync(
        AppConfig config,
        CancellationToken cancellationToken);
}
```

### Startup coordinator

`StartupCoordinator` is the only component deciding between tray-only startup and Welcome Setup. Its dependencies are status-bearing configuration loading, the model catalog/file manager, the shared inference engine, a runtime factory, and a Welcome Setup host.

Its state transitions are:

```text
Checking
  -> Ready -> StartingRuntime -> Running
  -> SetupRequired -> ShowingSetup -> FinishingSetup -> StartingRuntime -> Running
  -> FatalError -> ShowingSetup
  -> Exiting
```

The coordinator loads configuration once, checks the configured model, and attempts to load it. It creates the normal runtime only after readiness is established. Repeated Finish/Retry requests are serialized; stale operations are cancelled when the app exits.

### Welcome Setup

`WelcomeSetupViewModel` owns presentation state and delegates persistence/download/load operations through interfaces. It exposes typed state rather than letting the window perform I/O:

```csharp
public enum WelcomeSetupStage
{
    NeedsSettings,
    NeedsModel,
    Downloading,
    Loading,
    Error,
    Ready
}

public sealed record WelcomeSetupCompletion(AppConfig Config);
```

The view model exposes editable theme, target language, result action, catalog selection, progress, status text, error text, `CanFinish`, and `IsBusy`. `FinishAsync` validates and saves settings, downloads the model only when it is absent, loads it, and returns a completion only after the inference engine reports loaded status for the selected model.

`WelcomeSetupWindow` is a thin Avalonia view. Its close behavior calls the coordinator's Exit action until setup completes. On successful completion, the coordinator marks the host complete before closing it, preventing the close handler from shutting down the newly started runtime.

## Data flow

```text
Avalonia lifecycle
  -> StartupCoordinator.StartAsync
     -> JsonConfigStore.LoadWithStatusAsync
     -> configured-model file check
     -> shared inference-engine load
        -> ready: ButchiRuntimeFactory.CreateAsync -> StartTray
        -> not ready: WelcomeSetupWindow
           -> save config -> download if needed -> load model
           -> ButchiRuntimeFactory.CreateAsync -> StartTray -> close Welcome
```

All disk, SQLite, download, and model-load operations are awaited. No `GetAwaiter().GetResult()` remains in interactive startup or shutdown.

## Error handling and privacy

- Missing configuration is normal first-run state, not an exception.
- Invalid configuration opens setup with defaults and an explanation that settings must be saved again.
- Configuration access failure is shown with Retry and Exit; saving is attempted only on explicit Finish.
- Partial model downloads continue using the existing `.download` cleanup behavior.
- Model-load failures retain the selected model and show Retry.
- Fatal runtime-composition errors remain visible in Welcome Setup instead of leaving a trayless background process.
- Diagnostic output contains readiness stages and exception categories only. Configuration values, selected text, prompts, history content, and generated content are not logged.

## Testing

Tests remain headless where possible:

- configuration status tests cover ready, missing, malformed, and inaccessible inputs;
- startup policy/coordinator tests cover ready tray-only startup, each setup-required reason, retry, successful transition, close-before-complete exit, and idempotent concurrent requests;
- Welcome Setup view-model tests cover defaults, validation, save, download, progress, load, retry, and completion;
- an Avalonia construction/contract test ensures the one-screen window exposes settings, model, Finish, Retry, and Exit controls;
- an integration test uses a genuinely asynchronous config store to prove startup returns control instead of deadlocking;
- release-probe readiness must be derived from the coordinator/runtime checks, not enum membership;
- the complete solution test suite and win-x64/win-arm64 publish smoke remain required.

## Non-goals

- redesigning the management window or popover;
- adding a multi-page wizard;
- allowing inference actions without a loaded model;
- changing model catalog contents;
- changing persistence formats;
- moving history errors into startup setup;
- adding Store or MSIX behavior unrelated to startup verification.
