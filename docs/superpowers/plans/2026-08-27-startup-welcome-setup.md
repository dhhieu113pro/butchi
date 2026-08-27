# Startup Welcome Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace blocking interactive startup with an asynchronous coordinator that stays tray-only when ready and shows one mandatory Welcome Setup screen when settings or the configured local model are not ready.

**Architecture:** `App` performs only Avalonia lifecycle setup and launches `StartupCoordinator.RunAsync` without blocking. A status-bearing configuration API and `StartupReadinessService` decide whether to create `ButchiRuntime` or show `WelcomeSetupWindow`; successful one-screen setup saves settings, downloads/loads the chosen model, and transitions to the normal tray runtime in the same process.

**Tech Stack:** .NET 10, C# 14, Avalonia 12.1.1, System.Text.Json, LLamaSharp 0.27.0, xUnit 2.9.3

**Spec:** `docs/superpowers/specs/2026-08-27-startup-welcome-setup-design.md`

## Global Constraints

- Normal ready startup remains tray-only.
- Welcome Setup is one screen, not a wizard.
- Setup is mandatory when configuration or the configured model is not ready; closing before completion exits the app.
- There is exactly one shared `LLamaSharpInferenceEngine` for readiness checks, setup, and the running application.
- No interactive startup or shutdown path may call `GetAwaiter().GetResult()`, `.Result`, or `.Wait()`.
- History failures remain page-level errors and do not force Welcome Setup.
- Readiness diagnostics contain stages/error categories only; never configuration values, selected text, prompts, history, or generated content.
- Existing config JSON and model catalog formats remain compatible.

## File structure

- `src/Butchi.Infrastructure/JsonConfigStore.cs`: status-bearing configuration load with compatibility fallback.
- `src/Butchi.App/Startup/StartupReadiness.cs`: readiness enums, immutable result, and evaluator.
- `src/Butchi.App/Startup/StartupCoordinator.cs`: startup state machine and runtime/setup routing.
- `src/Butchi.App/Startup/StartupApplicationServices.cs`: owns shared paths, config, HTTP, inference, model, and history services.
- `src/Butchi.App/Startup/ButchiRuntime.cs`: owns normal windows, tray objects, and asynchronous cleanup.
- `src/Butchi.App/Startup/ButchiRuntimeFactory.cs`: asynchronously composes normal view models and windows.
- `src/Butchi.App/Startup/ScreenshotStartup.cs`: preserves deterministic screenshot modes without entering readiness/setup/tray startup.
- `src/Butchi.App/Startup/WelcomeSetupViewModel.cs`: one-screen setup state and operations.
- `src/Butchi.App/Startup/WelcomeSetupWindow.cs`: thin Avalonia setup UI and completion/exit host.
- `src/Butchi.App/App.cs`: minimal Avalonia lifecycle wiring and non-blocking shutdown.
- `src/Butchi.App/Diagnostics/ReleaseProbe.cs`: coordinator-derived startup readiness evidence.
- Focused tests under `tests/Butchi.Infrastructure.Tests` and `tests/Butchi.App.Tests` mirror each production unit.

---

### Task 1: Preserve configuration readiness status

**Files:**
- Modify: `src/Butchi.Infrastructure/JsonConfigStore.cs`
- Create: `src/Butchi.Infrastructure/Properties/AssemblyInfo.cs`
- Modify: `tests/Butchi.Infrastructure.Tests/PersistenceCompatibilityTests.cs`

**Interfaces:**
- Consumes: existing `AppConfig`, `AppPaths.ConfigPath`, and JSON serializer options.
- Produces: `ConfigLoadState`, `ConfigLoadResult`, and `JsonConfigStore.LoadWithStatusAsync(CancellationToken)`.

- [ ] **Step 1: Write failing configuration-status tests**

Add tests using a unique temporary `AppPaths` root and deterministic files:

```csharp
[Fact]
public async Task LoadWithStatus_distinguishes_missing_ready_and_invalid_config()
{
    using var root = new TemporaryDirectory();
    var paths = new AppPaths(root.Path);
    var store = new JsonConfigStore(paths);

    var missing = await store.LoadWithStatusAsync();
    Assert.Equal(ConfigLoadState.Missing, missing.State);
    Assert.Equal(AppConfig.Default, missing.Config);

    await store.SaveAsync(AppConfig.Default with { TargetLanguage = "Japanese" });
    var ready = await store.LoadWithStatusAsync();
    Assert.Equal(ConfigLoadState.Ready, ready.State);
    Assert.Equal("Japanese", ready.Config.TargetLanguage);

    await File.WriteAllTextAsync(paths.ConfigPath, "{not-json");
    var invalid = await store.LoadWithStatusAsync();
    Assert.Equal(ConfigLoadState.Invalid, invalid.State);
    Assert.Equal(AppConfig.Default, invalid.Config);
    Assert.Equal(nameof(JsonException), invalid.ErrorCode);
}
```

Add an inaccessible/read-failure test through an internal injectable stream opener rather than relying on platform ACL behavior:

```csharp
[Fact]
public async Task LoadWithStatus_maps_io_failure_without_exposing_content()
{
    using var root = new TemporaryDirectory();
    var paths = new AppPaths(root.Path);
    paths.EnsureDirectories();
    await File.WriteAllTextAsync(paths.ConfigPath, "{}");
    var store = new JsonConfigStore(
        paths,
        _ => throw new IOException("secret body"));
    var result = await store.LoadWithStatusAsync();

    Assert.Equal(ConfigLoadState.Unavailable, result.State);
    Assert.Equal(nameof(IOException), result.ErrorCode);
    Assert.DoesNotContain("secret body", result.ErrorCode);
}
```

Add this nested test helper so cleanup is explicit and local to the test file:

```csharp
private sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory() =>
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"butchi-tests-{Guid.NewGuid():N}");

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
```

- [ ] **Step 2: Run the focused test and verify failure**

Run:

```powershell
dotnet test tests/Butchi.Infrastructure.Tests/Butchi.Infrastructure.Tests.csproj --filter "FullyQualifiedName~PersistenceCompatibilityTests"
```

Expected: compilation fails because `ConfigLoadState`, `ConfigLoadResult`, and `LoadWithStatusAsync` do not exist.

- [ ] **Step 3: Implement the status-bearing load API**

Add these public types in `JsonConfigStore.cs`:

```csharp
public enum ConfigLoadState { Ready, Missing, Invalid, Unavailable }

public sealed record ConfigLoadResult(
    AppConfig Config,
    ConfigLoadState State,
    string? ErrorCode = null);
```

Implement the status method and preserve the existing caller contract:

```csharp
public async Task<ConfigLoadResult> LoadWithStatusAsync(CancellationToken cancellationToken = default)
{
    if (!File.Exists(_paths.ConfigPath))
        return new(AppConfig.Default, ConfigLoadState.Missing);

    try
    {
        await using var stream = _openRead(_paths.ConfigPath);
        var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, _options, cancellationToken)
            .ConfigureAwait(false);
        return config is null
            ? new(AppConfig.Default, ConfigLoadState.Invalid, nameof(JsonException))
            : new(config, ConfigLoadState.Ready);
    }
    catch (JsonException ex)
    {
        return new(AppConfig.Default, ConfigLoadState.Invalid, ex.GetType().Name);
    }
    catch (IOException ex)
    {
        return new(AppConfig.Default, ConfigLoadState.Unavailable, ex.GetType().Name);
    }
    catch (UnauthorizedAccessException ex)
    {
        return new(AppConfig.Default, ConfigLoadState.Unavailable, ex.GetType().Name);
    }
}

public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default) =>
    (await LoadWithStatusAsync(cancellationToken).ConfigureAwait(false)).Config;
```

Use an internal constructor overload accepting `Func<string, Stream>` for tests, while the public constructor passes `File.OpenRead`. Create `Properties/AssemblyInfo.cs` with:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Butchi.Infrastructure.Tests")]
```

- [ ] **Step 4: Run infrastructure tests**

Run:

```powershell
dotnet test tests/Butchi.Infrastructure.Tests/Butchi.Infrastructure.Tests.csproj
```

Expected: all infrastructure tests pass and existing fallback behavior remains unchanged.

- [ ] **Step 5: Commit the configuration result**

```powershell
git add src/Butchi.Infrastructure/JsonConfigStore.cs src/Butchi.Infrastructure/Properties/AssemblyInfo.cs tests/Butchi.Infrastructure.Tests/PersistenceCompatibilityTests.cs
git commit -m "feat: expose config startup readiness"
```

---

### Task 2: Define and test startup readiness

**Files:**
- Create: `src/Butchi.App/Startup/StartupReadiness.cs`
- Create: `tests/Butchi.App.Tests/StartupReadinessTests.cs`
- Modify: `src/Butchi.App/Settings/JsonAppConfigStoreAdapter.cs`

**Interfaces:**
- Consumes: `JsonConfigStore.LoadWithStatusAsync`, `IModelManager`, `ModelOption`, `IInferenceEngine`, and `AppConfig`.
- Produces: `StartupReadinessReason`, `StartupReadinessResult`, `IStartupReadinessService`, and `StartupReadinessService.CheckAsync(CancellationToken)`.

- [ ] **Step 1: Write failing readiness tests**

Cover missing/invalid config, missing model, model-load failure, and ready startup:

```csharp
[Theory]
[InlineData(ConfigLoadState.Missing, StartupReadinessReason.SettingsMissing)]
[InlineData(ConfigLoadState.Invalid, StartupReadinessReason.SettingsInvalid)]
[InlineData(ConfigLoadState.Unavailable, StartupReadinessReason.SettingsUnavailable)]
public async Task Non_ready_config_requires_setup(
    ConfigLoadState state,
    StartupReadinessReason expected)
{
    var service = CreateService(configState: state);
    var result = await service.CheckAsync(CancellationToken.None);
    Assert.False(result.IsReady);
    Assert.Equal(expected, result.Reason);
}

[Fact]
public async Task Existing_configured_model_is_loaded_before_ready_result()
{
    var service = CreateService(configState: ConfigLoadState.Ready, modelExists: true);
    var result = await service.CheckAsync(CancellationToken.None);
    Assert.True(result.IsReady);
    Assert.Equal(1, FakeModelManager.LoadCalls);
    Assert.True(FakeModelManager.Status.IsLoaded);
}
```

Also assert that model absence returns `ModelMissing`, a thrown load returns `ModelLoadFailed` with exception type only, and a configured model outside the catalog returns `ModelMissing`.

- [ ] **Step 2: Run the readiness tests and verify failure**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "FullyQualifiedName~StartupReadinessTests"
```

Expected: compilation fails because the startup readiness types do not exist.

- [ ] **Step 3: Implement the readiness model and evaluator**

Use immutable results:

```csharp
public enum StartupReadinessReason
{
    Ready,
    SettingsMissing,
    SettingsInvalid,
    SettingsUnavailable,
    ModelMissing,
    ModelLoadFailed,
    RuntimeFailed
}

public sealed record StartupReadinessResult(
    bool IsReady,
    AppConfig Config,
    StartupReadinessReason Reason,
    string? ErrorCode = null);

public interface IStartupReadinessService
{
    ValueTask<StartupReadinessResult> CheckAsync(CancellationToken cancellationToken);
}
```

`StartupReadinessService.CheckAsync` must:

1. load status once;
2. map non-ready config state without trying to load a model;
3. locate the exact configured catalog item;
4. return `ModelMissing` if its local file is absent;
5. call `IModelManager.LoadAsync` and verify `GetStatus()` matches the configured repo/file;
6. return `ModelLoadFailed` using only `ex.GetType().Name` when load fails.

Expose status loading from the adapter without changing `IAppConfigStore` used by settings pages:

```csharp
public interface IStartupConfigStore : IAppConfigStore
{
    ValueTask<ConfigLoadResult> LoadWithStatusAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Run readiness and existing model tests**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "FullyQualifiedName~StartupReadinessTests|FullyQualifiedName~ModelManagementViewModelTests|FullyQualifiedName~ModelsViewModelTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit readiness evaluation**

```powershell
git add src/Butchi.App/Startup/StartupReadiness.cs src/Butchi.App/Settings/JsonAppConfigStoreAdapter.cs tests/Butchi.App.Tests/StartupReadinessTests.cs
git commit -m "feat: evaluate interactive startup readiness"
```

---

### Task 3: Build the one-screen Welcome Setup state model

**Files:**
- Create: `src/Butchi.App/Startup/WelcomeSetupViewModel.cs`
- Create: `tests/Butchi.App.Tests/WelcomeSetupViewModelTests.cs`

**Interfaces:**
- Consumes: `IAppConfigStore`, `IModelManager`, `ModelOption`, `ModelDownloadProgress`, and the initial `StartupReadinessResult`.
- Produces: `WelcomeSetupStage`, `WelcomeSetupCompletion`, `IWelcomeSetupViewModelFactory`, and `WelcomeSetupViewModel.FinishAsync(CancellationToken)`.

Define the factory used by the coordinator:

```csharp
public interface IWelcomeSetupViewModelFactory
{
    WelcomeSetupViewModel Create(StartupReadinessResult readiness);
}
```

- [ ] **Step 1: Write failing setup-state tests**

Write tests with asynchronous fakes (`TaskCompletionSource` with `RunContinuationsAsynchronously`) so they cannot pass only because operations complete synchronously:

```csharp
[Fact]
public async Task Finish_saves_settings_downloads_missing_model_and_loads_it()
{
    var model = ModelCatalog.Options[0];
    var store = new FakeConfigStore(AppConfig.Default);
    var manager = new FakeModelManager(model) { Downloaded = false };
    var vm = WelcomeSetupViewModel.Create(SetupRequired(), store, manager);

    vm.TargetLanguage = "Japanese";
    vm.SelectModel(model);
    var completion = await vm.FinishAsync(CancellationToken.None);

    Assert.Equal("Japanese", completion.Config.TargetLanguage);
    Assert.Equal(new[] { "download", "load" }, manager.Operations);
    Assert.Equal(WelcomeSetupStage.Ready, vm.Stage);
    Assert.True(vm.CanFinish);
    Assert.Null(vm.ErrorMessage);
}
```

Add tests for invalid blank target language, save failure, download failure, load failure, progress updates, retry after failure, no repeat download for a local model, and suppression of a second concurrent `FinishAsync` call.

- [ ] **Step 2: Run the setup view-model tests and verify failure**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "FullyQualifiedName~WelcomeSetupViewModelTests"
```

Expected: compilation fails because the setup types do not exist.

- [ ] **Step 3: Implement typed setup state**

Define:

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

Implement `INotifyPropertyChanged` with these public properties: `Theme`, `TargetLanguage`, `ResultAction`, `Catalog`, `SelectedModel`, `DownloadProgress`, `Stage`, `StatusText`, `ErrorMessage`, `IsBusy`, and `CanFinish`. Serialize Finish operations with `SemaphoreSlim(1, 1)` and always release it in `finally`.

The core operation order must be explicit:

```csharp
var config = _initialConfig with
{
    Theme = Theme,
    TargetLanguage = AppConfig.NormalizeTargetLanguage(TargetLanguage),
    ResultAction = ResultAction,
    ModelRepo = SelectedModel.Repo,
    ModelFile = SelectedModel.File
};

await _configStore.SaveAsync(config, cancellationToken);
if (!_modelManager.IsDownloaded(SelectedModel))
    await _modelManager.DownloadAsync(SelectedModel, progress, cancellationToken);
await _modelManager.LoadAsync(SelectedModel, cancellationToken);

var status = _modelManager.GetStatus();
if (!status.IsLoaded || status.ModelRepo != SelectedModel.Repo || status.ModelFile != SelectedModel.File)
    throw new InvalidOperationException("Selected model did not become ready.");
```

Map expected exceptions to concise user-facing messages while retaining only exception type in diagnostic state. Do not include paths, JSON, response bodies, or user settings in error text.

- [ ] **Step 4: Run setup and existing settings/model tests**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "FullyQualifiedName~WelcomeSetupViewModelTests|FullyQualifiedName~GeneralSettingsViewModelTests|FullyQualifiedName~ModelManagementViewModelTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit setup state**

```powershell
git add src/Butchi.App/Startup/WelcomeSetupViewModel.cs tests/Butchi.App.Tests/WelcomeSetupViewModelTests.cs
git commit -m "feat: add welcome setup state"
```

---

### Task 4: Add the dedicated Welcome Setup window and host

**Files:**
- Create: `src/Butchi.App/Startup/WelcomeSetupWindow.cs`
- Create: `tests/Butchi.App.Tests/WelcomeSetupUiContractTests.cs`

**Interfaces:**
- Consumes: `WelcomeSetupViewModel` and `WelcomeSetupCompletion`.
- Produces: `IWelcomeSetupHost.ShowAsync(WelcomeSetupViewModel, CancellationToken)` and a close-before-complete exit signal.

- [ ] **Step 1: Write failing UI-host and source contract tests**

Use a host test to prove completion and premature close differ:

```csharp
[Fact]
public async Task Host_returns_completion_only_after_finish_succeeds()
{
    var host = new FakeWelcomeSetupHost();
    var pending = host.ShowAsync(CreateViewModel(), CancellationToken.None).AsTask();
    Assert.False(pending.IsCompleted);

    host.Complete(new WelcomeSetupCompletion(AppConfig.Default));
    Assert.NotNull(await pending);
}
```

Add a repository contract test that reads `WelcomeSetupWindow.cs` and asserts it contains one top-level settings section, one model section, `Finish setup`, `Retry`, and `Exit`, and does not contain wizard navigation labels `Next` or `Back`.

- [ ] **Step 2: Run the UI contract test and verify failure**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "FullyQualifiedName~WelcomeSetupUiContractTests"
```

Expected: failure because `WelcomeSetupWindow.cs` and the host contract do not exist.

- [ ] **Step 3: Implement the thin Avalonia window**

Create a centered, taskbar-visible window using existing `ButchiTheme` and `BrandAssets` resources. Use one scrollable surface with two cards:

```text
Welcome to Butchi
Private local translation and rewriting

[Settings]
Theme | Target language | Result action

[Local model]
Model selector | size/local state | progress/status

[Error + Retry when applicable]
[Exit] [Finish setup]
```

Wire button events to awaited view-model operations through async event handlers that catch exceptions already represented by the view model. Implement the host with `TaskCompletionSource<WelcomeSetupCompletion?>` created using `TaskCreationOptions.RunContinuationsAsynchronously`:

```csharp
public interface IWelcomeSetupHost
{
    ValueTask<WelcomeSetupCompletion?> ShowAsync(
        WelcomeSetupViewModel viewModel,
        CancellationToken cancellationToken);
}
```

Return `null` for Exit or closing before completion. Set a private `_completed` flag before closing after successful Finish so the close handler cannot overwrite the successful result.

- [ ] **Step 4: Run UI contract and app view-model tests**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "FullyQualifiedName~WelcomeSetupUiContractTests|FullyQualifiedName~WelcomeSetupViewModelTests|FullyQualifiedName~BrandingContractTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit the Welcome Setup UI**

```powershell
git add src/Butchi.App/Startup/WelcomeSetupWindow.cs tests/Butchi.App.Tests/WelcomeSetupUiContractTests.cs src/Butchi.App/Branding/BrandAssets.cs
git commit -m "feat: add one-screen welcome setup"
```

---

### Task 5: Extract runtime ownership and coordinate startup

**Files:**
- Create: `src/Butchi.App/Startup/StartupApplicationServices.cs`
- Create: `src/Butchi.App/Startup/ButchiRuntime.cs`
- Create: `src/Butchi.App/Startup/ButchiRuntimeFactory.cs`
- Create: `src/Butchi.App/Startup/ScreenshotStartup.cs`
- Create: `src/Butchi.App/Startup/StartupCoordinator.cs`
- Create: `tests/Butchi.App.Tests/StartupCoordinatorTests.cs`
- Create: `tests/Butchi.App.Tests/InteractiveStartupRegressionTests.cs`
- Modify: `src/Butchi.App/App.cs`

**Interfaces:**
- Consumes: readiness service from Task 2, Welcome host/view model from Tasks 3-4, existing management/popover/tray classes, and one shared inference engine.
- Produces: `IButchiRuntime`, `IButchiRuntimeFactory`, `StartupCoordinator.RunAsync(CancellationToken)`, and non-blocking `App` startup/shutdown.

Define the shared runtime/state contracts exactly once in `ButchiRuntime.cs` and `StartupCoordinator.cs`:

```csharp
public interface IButchiRuntime : IAsyncDisposable
{
    bool IsTrayStarted { get; }
    void StartTray();
}

public interface IButchiRuntimeFactory
{
    ValueTask<IButchiRuntime> CreateAsync(
        AppConfig config,
        CancellationToken cancellationToken);
}

public enum StartupCoordinatorState
{
    NotStarted,
    Checking,
    ShowingSetup,
    StartingRuntime,
    Running,
    Exiting
}
```

The concrete `ButchiRuntimeFactory` additionally exposes screenshot-only composition without adding those methods to `IButchiRuntimeFactory`:

```csharp
public ValueTask<ManagementWindow> CreateManagementScreenshotAsync(
    ScreenshotRequest request,
    CancellationToken cancellationToken);

public PopoverWindow CreatePopoverScreenshot(
    string fixture,
    AppThemePreference theme);
```

- [ ] **Step 1: Write failing coordinator behavior tests**

Use fakes to cover ready, setup-required, exit, successful transition, runtime failure routed back to visible setup, and idempotence:

```csharp
[Fact]
public async Task Ready_startup_starts_tray_without_showing_welcome()
{
    var readiness = new FakeReadiness(Ready(AppConfig.Default));
    var welcome = new FakeWelcomeHost();
    var runtime = new FakeRuntime();
    var coordinator = CreateCoordinator(readiness, welcome, runtime);

    await coordinator.RunAsync(CancellationToken.None);

    Assert.Equal(0, welcome.ShowCalls);
    Assert.Equal(1, runtime.StartTrayCalls);
}

[Fact]
public async Task Setup_completion_transitions_to_tray_in_same_process()
{
    var readiness = new FakeReadiness(ModelMissing(AppConfig.Default));
    var welcome = new FakeWelcomeHost(new WelcomeSetupCompletion(AppConfig.Default));
    var runtime = new FakeRuntime();

    await CreateCoordinator(readiness, welcome, runtime).RunAsync(CancellationToken.None);

    Assert.Equal(1, welcome.ShowCalls);
    Assert.Equal(1, runtime.StartTrayCalls);
    Assert.False(runtime.IsDisposed);
}
```

The regression test must use a config store whose load completes asynchronously after control returns. Assert `App`'s startup launcher returns before the load completes and finishes after the fake is released; this directly catches the original sync-over-async deadlock.

- [ ] **Step 2: Run coordinator tests and verify failure**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "FullyQualifiedName~StartupCoordinatorTests|FullyQualifiedName~InteractiveStartupRegressionTests"
```

Expected: compilation fails because runtime/coordinator types and the non-blocking startup entry point do not exist.

- [ ] **Step 3: Implement shared service ownership**

`StartupApplicationServices` constructs only cheap synchronous objects in its constructor: `AppPaths`, config stores, `HttpClient`, downloader, the single inference engine, model manager, and SQLite history adapter. Expose those dependencies as read-only properties and dispose in this order:

```csharp
public async ValueTask DisposeAsync()
{
    await InferenceEngine.DisposeAsync();
    HttpClient.Dispose();
}
```

Guard disposal with `Interlocked.Exchange` so shutdown is idempotent.

- [ ] **Step 4: Implement asynchronous normal runtime composition**

Move the existing General, Prompts, Models, History, About, management window, popover, tray menu, and tray icon construction from `App` into `ButchiRuntimeFactory.CreateAsync`. Await every view-model factory normally:

```csharp
var general = await GeneralSettingsViewModel.CreateAsync(configStore, cancellationToken);
var prompts = await PromptsViewModel.CreateAsync(configStore, cancellationToken);
var models = await ModelManagementViewModel.CreateAsync(modelManager, configStore, cancellationToken);
var history = await HistoryViewModel.CreateAsync(historyStore, clipboard, configStore, cancellationToken);
```

`ButchiRuntime.StartTray()` must create the real `TrayIcon`, attach it with `TrayIcon.SetIcons(application, icons)`, and set an internal `IsTrayStarted` marker only after attachment. `DisposeAsync` disposes the tray, destroys the popover, hides the management window, and leaves shared engine/client disposal to `StartupApplicationServices`.

- [ ] **Step 5: Implement the coordinator loop**

The coordinator owns one `SemaphoreSlim` run gate and follows this exact control flow:

```csharp
var readiness = await _readiness.CheckAsync(cancellationToken);
var config = readiness.Config;

if (!readiness.IsReady)
{
    var setup = _welcomeViewModelFactory.Create(readiness);
    var completion = await _welcomeHost.ShowAsync(setup, cancellationToken);
    if (completion is null)
    {
        _shutdown();
        return;
    }
    config = completion.Config;
}

try
{
    _runtime = await _runtimeFactory.CreateAsync(config, cancellationToken);
    _runtime.StartTray();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    var failure = new StartupReadinessResult(
        false, config, StartupReadinessReason.RuntimeFailed, ex.GetType().Name);
    var retry = await _welcomeHost.ShowAsync(
        _welcomeViewModelFactory.Create(failure), cancellationToken);
    if (retry is null) _shutdown();
    else await StartRuntimeAsync(retry.Config, cancellationToken);
}
```

Factor `StartRuntimeAsync` so Retry does not recursively duplicate the whole coordinator. Limit runtime retry to one active attempt at a time; the UI remains available for subsequent explicit retries.

- [ ] **Step 6: Reduce `App` to lifecycle wiring**

`OnFrameworkInitializationCompleted` must contain no data/model/history/view-model construction:

```csharp
public override void OnFrameworkInitializationCompleted()
{
    ButchiTheme.Initialize(this);
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

    base.OnFrameworkInitializationCompleted();

    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
    {
        _shutdownCts = new CancellationTokenSource();
        _startupTask = StartInteractiveAsync(_shutdownCts.Token);
    }
}
```

`StartInteractiveAsync` catches non-cancellation exceptions and invokes the coordinator's visible fatal-error route. Replace synchronous cleanup with:

```csharp
public void Shutdown() => _ = ShutdownAsync();

private async Task ShutdownAsync()
{
    _shutdownCts?.Cancel();
    if (_coordinator is not null) await _coordinator.DisposeAsync();
    if (_services is not null) await _services.DisposeAsync();
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.Shutdown();
}
```

Prevent re-entry with `Interlocked.Exchange`. Preserve screenshot modes through `ScreenshotStartup`; they must not show Welcome Setup or create a tray.

- [ ] **Step 7: Isolate screenshot startup from interactive readiness**

Move the existing `--screenshot` and `--screenshot-popover` branches into `ScreenshotStartup.RunAsync(string[] args, CancellationToken)`. It uses `ButchiRuntimeFactory`'s screenshot-specific composition methods, awaits a `TaskCompletionSource` completed by the existing `ScreenshotRunner` callback, and never calls the readiness service, Welcome host, or `StartTray()`:

```csharp
public async Task<bool> TryRunAsync(string[] args, CancellationToken cancellationToken)
{
    if (!ScreenshotRequest.TryParse(args, out var request) &&
        Array.IndexOf(args, "--screenshot-popover") < 0)
        return false;

    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    if (request is not null)
    {
        var window = await _runtimeFactory.CreateManagementScreenshotAsync(request, cancellationToken);
        ScreenshotRunner.Run(request, window, () => completion.TrySetResult());
    }
    else
    {
        var outputIndex = Array.IndexOf(args, "--screenshot-popover");
        var outputPath = RequireOptionValue(args, outputIndex, "--screenshot-popover");
        var fixture = GetOptionValue(args, "--fixture") ?? "success";
        var theme = ScreenshotRequest.ParseTheme(GetOptionValue(args, "--theme") ?? "system");
        var window = _runtimeFactory.CreatePopoverScreenshot(fixture, theme);
        ScreenshotRunner.RunPopover(outputPath, window, () => completion.TrySetResult());
    }
    await completion.Task.WaitAsync(cancellationToken);
    return true;
}
```

Move the existing `GetOptionValue` behavior and missing-output validation from `App` into private helpers in `ScreenshotStartup`. Create `StartupApplicationServices` first, then call `TryRunAsync` before constructing or invoking the readiness coordinator. Dispose services after screenshot completion. Add assertions to existing `ScreenshotModeTests` that screenshot mode does not invoke readiness, Welcome Setup, or tray startup.

Use these helper signatures so option parsing remains deterministic:

```csharp
private static string? GetOptionValue(string[] args, string option);
private static string RequireOptionValue(string[] args, int optionIndex, string option);
```

`RequireOptionValue` throws `ArgumentException($"{option} requires an output path.", nameof(args))` when the option is last or its following value is blank.

- [ ] **Step 8: Run coordinator and complete app tests**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj
```

Expected: all app tests pass, including the genuinely asynchronous deadlock regression.

- [ ] **Step 9: Search for forbidden interactive blocking**

Run:

```powershell
rg -n "GetAwaiter\(\)\.GetResult|\.Result\b|\.Wait\(" src/Butchi.App -g "*.cs"
```

Expected: no matches in `App.cs`, `Startup/`, or any interactive startup/shutdown path. A release-probe-only match must be removed in Task 6 rather than waived.

- [ ] **Step 10: Commit asynchronous startup composition**

```powershell
git add src/Butchi.App/App.cs src/Butchi.App/Startup tests/Butchi.App.Tests/StartupCoordinatorTests.cs tests/Butchi.App.Tests/InteractiveStartupRegressionTests.cs tests/Butchi.App.Tests/ScreenshotModeTests.cs
git commit -m "fix: make interactive startup non-blocking"
```

---

### Task 6: Make release readiness exercise the startup state machine

**Files:**
- Modify: `src/Butchi.App/Diagnostics/ReleaseProbe.cs`
- Modify: `src/Butchi.App/Program.cs`
- Modify: `tests/Butchi.App.Tests/InstalledAppProbeTests.cs`
- Modify: `tests/Butchi.App.Tests/PackagedBehaviorProbeTests.cs`

**Interfaces:**
- Consumes: `StartupCoordinator`, `StartupReadinessResult`, and `IButchiRuntime.IsTrayStarted` from Task 5.
- Produces: truthful privacy-safe `firstRunCompositionReady` and `trayReady` probe results.

- [ ] **Step 1: Replace enum-membership expectations with startup-state expectations**

Update tests so `trayReady` cannot be true merely because `TrayCommand` values exist:

```csharp
[Fact]
public void Release_probe_does_not_derive_tray_readiness_from_command_enum()
{
    var source = File.ReadAllText(FindSource("src", "Butchi.App", "Diagnostics", "ReleaseProbe.cs"));
    Assert.DoesNotContain("Enum.GetValues<TrayCommand>", source, StringComparison.Ordinal);
    Assert.Contains("IsTrayStarted", source, StringComparison.Ordinal);
    Assert.Contains("StartupReadinessReason", source, StringComparison.Ordinal);
}
```

Add a probe-runner unit test using an asynchronous fake readiness service and fake runtime. Assert the result reports `FirstRunCompositionReady=true` only after the coordinator reaches Running and `TrayReady=true` only after `StartTray()` sets `IsTrayStarted`.

- [ ] **Step 2: Run probe tests and verify failure**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "FullyQualifiedName~InstalledAppProbeTests|FullyQualifiedName~PackagedBehaviorProbeTests"
```

Expected: failure because `ReleaseProbe` still derives tray readiness from enum membership.

- [ ] **Step 3: Implement coordinator-derived probe results**

Move probe execution behind an asynchronous `RunAsync` path all the way through `Program.Main`. Change `Main` to return `Task<int>`:

```csharp
[STAThread]
public static async Task<int> Main(string[] args)
{
    StartupArgs = args;
    if (ReleaseProbe.TryParse(args, out var outputPath))
        return await ReleaseProbe.RunAsync(outputPath!);

    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    return 0;
}
```

In probe mode, use the same coordinator contracts with deterministic probe doubles for UI and model download; do not synthesize keyboard/mouse input or user content. Derive flags from observed coordinator/runtime state:

```csharp
var firstRunCompositionReady = coordinator.State == StartupCoordinatorState.Running;
var trayReady = runtime.IsTrayStarted;
```

Keep package identity/version transport and existing privacy-safe fields compatible with installed-package scripts.

- [ ] **Step 4: Run probe and app tests**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj
```

Expected: all app tests pass; probe tests prove a real startup state transition rather than command availability.

- [ ] **Step 5: Commit truthful startup probing**

```powershell
git add src/Butchi.App/Diagnostics/ReleaseProbe.cs src/Butchi.App/Program.cs tests/Butchi.App.Tests/InstalledAppProbeTests.cs tests/Butchi.App.Tests/PackagedBehaviorProbeTests.cs
git commit -m "test: exercise startup readiness in release probe"
```

---

### Task 7: Complete verification and documentation alignment

**Files:**
- Modify: `docs/production-cutover.md` to record tray-only ready startup and mandatory Welcome Setup behavior.
- Modify: `docs/superpowers/specs/2026-08-26-task10-management-ui-design.md` only by adding a short supersession link to the new startup design; do not rewrite historical decisions.

**Interfaces:**
- Consumes: completed startup implementation and existing release/publish scripts.
- Produces: verified solution and aligned operator/user documentation.

- [ ] **Step 1: Add a startup behavior contract test for documentation**

Add an assertion to `ProductionCutoverContractTests` that production documentation contains `Welcome Setup`, `tray-only`, and `startup readiness`, ensuring the behavior does not silently regress in release instructions.

- [ ] **Step 2: Run the documentation contract and verify failure**

Run:

```powershell
dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj --filter "FullyQualifiedName~ProductionCutoverContractTests"
```

Expected: failure until the production document describes the new startup behavior.

- [ ] **Step 3: Update concise startup documentation**

Document these exact operator-visible outcomes:

```text
- Ready settings + successfully loaded configured model: tray-only startup.
- Missing/invalid settings or missing/failed model: one Welcome Setup window.
- Closing incomplete setup exits Butchi.
- Successful setup transitions to tray operation without restart.
```

Add a supersession note to the Task 10 design pointing to `2026-08-27-startup-welcome-setup-design.md` for authoritative startup behavior.

- [ ] **Step 4: Run the complete solution test suite**

Run:

```powershell
dotnet test Butchi.slnx -c Release
```

Expected: every test project passes with zero failures.

- [ ] **Step 5: Publish Windows x64 and ARM64 candidates**

Run:

```powershell
dotnet publish src/Butchi.App/Butchi.App.csproj -c Release -r win-x64 --self-contained true
dotnet publish src/Butchi.App/Butchi.App.csproj -c Release -r win-arm64 --self-contained true
```

Expected: both publishes succeed and each output contains `Butchi.App.exe`, `Butchi.App.dll`, Avalonia native dependencies, the Butchi logo resource, and LLamaSharp runtime assets for its RID.

- [ ] **Step 6: Run production cutover validation**

Run:

```powershell
pwsh -File scripts/verify-production-cutover.ps1
```

Expected: validation exits 0 and no readiness gate is downgraded to a warning.

- [ ] **Step 7: Review the final diff for scope and blocking calls**

Run:

```powershell
git diff --check
rg -n "GetAwaiter\(\)\.GetResult|\.Result\b|\.Wait\(" src/Butchi.App -g "*.cs"
git status --short
```

Expected: no whitespace errors, no blocking calls in interactive app code, and only intended startup/tests/docs changes remain.

- [ ] **Step 8: Commit verification documentation**

```powershell
git add docs/production-cutover.md docs/superpowers/specs/2026-08-26-task10-management-ui-design.md tests/Butchi.App.Tests/ProductionCutoverContractTests.cs
git commit -m "docs: document welcome startup behavior"
```

- [ ] **Step 9: Request final code review**

Use `superpowers:requesting-code-review` against the complete branch diff. Resolve correctness findings, rerun Steps 4-7, and only then use `superpowers:finishing-a-development-branch` to choose merge/PR handling.
