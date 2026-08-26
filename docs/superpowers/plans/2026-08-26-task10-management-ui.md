# Task 10 Management UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one reusable native Avalonia management shell for Settings, History, Models, and Status/About, composed from one root service provider with exactly one shared inference engine.

**Architecture:** `Butchi.App` owns composition, management navigation, page view models, and thin Avalonia views. Existing `JsonConfigStore`, `SqliteHistoryStore`, model-management services, and inference contracts remain the source of truth; UI code orchestrates them rather than duplicating persistence or model logic. Task 10 is delivered through four independently green PR slices.

**Tech Stack:** .NET 10, C# 14, Avalonia Desktop, Microsoft.Extensions.DependencyInjection, xUnit

**Spec:** `docs/superpowers/specs/2026-08-26-task10-management-ui-design.md`

## Global Constraints
- `IInferenceEngine` is registered once as a singleton and never constructed by a window/page.
- Keep Core free of Avalonia and platform implementation dependencies.
- Reuse existing config/history/model stores and compatibility formats.
- Production behavior is TDD-first.
- Management UI is outside the fast popover path.
- Windows x64 and ARM64 publish-smoke checks stay green.
- Task 11 packaging/screenshot work is out of scope.

---

### Task 1: Root DI composition and singleton inference lifetime

**Files:**
- Create: `src/Butchi.App/Composition/ButchiServiceCollectionExtensions.cs`
- Create: `src/Butchi.App/Composition/AppServices.cs`
- Modify: `src/Butchi.App/Butchi.App.csproj`
- Modify: `src/Butchi.App/App.cs`
- Test: `tests/Butchi.App.Tests/CompositionTests.cs`

**Interfaces:**
- Produces: `IServiceProvider AppServices.Provider`, `IServiceCollection AddButchiApplicationServices(...)`.
- Guarantees: repeated `GetRequiredService<IInferenceEngine>()` returns the same instance.

- [ ] Write failing tests that build the app service collection, resolve `IInferenceEngine` twice, and assert `Assert.Same(first, second)`; resolve two consumers that both depend on `IInferenceEngine` and assert both hold the same instance.
- [ ] Run `dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj -c Release` and confirm RED because composition types do not exist.
- [ ] Add `Microsoft.Extensions.DependencyInjection` package reference if App does not already receive it directly.
- [ ] Implement `AddButchiApplicationServices` using `services.AddSingleton<IInferenceEngine>(...)` and register existing Infrastructure/model services with lifetimes appropriate to their state; do not instantiate an engine inside consumers.
- [ ] Update `App.OnFrameworkInitializationCompleted` to build/use one provider and resolve the existing persistent `PopoverWindow` dependencies from it.
- [ ] Run App tests and full solution tests; confirm GREEN.
- [ ] Commit `feat: add Task 10 root DI composition`.

### Task 2: Management shell navigation policy

**Files:**
- Create: `src/Butchi.App/Management/ManagementPage.cs`
- Create: `src/Butchi.App/Management/ManagementShellViewModel.cs`
- Create: `src/Butchi.App/Management/ManagementWindow.cs`
- Test: `tests/Butchi.App.Tests/ManagementShellViewModelTests.cs`

**Interfaces:**
- Produces: `ManagementPage` enum values `Settings`, `History`, `Models`, `Status`; `ManagementShellViewModel.SelectedPage`; `Select(ManagementPage page)`.
- `ManagementWindow` is reusable: callers select a page then show/activate the same instance.

- [ ] Write failing tests: default page is Settings; selecting History/Models/Status changes only `SelectedPage`; selecting the same page is idempotent; the app composition resolves one management-window holder/factory rather than creating a new window for each command.
- [ ] Run the focused test and confirm RED for missing management types.
- [ ] Implement the enum/view model with `INotifyPropertyChanged` and no Avalonia dependency in the view model.
- [ ] Implement a thin `ManagementWindow` with left navigation buttons bound to the shell view model and a single active content host. Keep page placeholders simple in this slice.
- [ ] Run App tests and full solution tests; confirm GREEN.
- [ ] Commit `feat: add reusable management shell`.

### Task 3: Settings working copy and reload classification

**Files:**
- Create: `src/Butchi.App/Settings/IAppConfigStore.cs`
- Create: `src/Butchi.App/Settings/JsonAppConfigStoreAdapter.cs`
- Create: `src/Butchi.App/Settings/SettingsChangePolicy.cs`
- Create: `src/Butchi.App/Settings/SettingsViewModel.cs`
- Test: `tests/Butchi.App.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- `IAppConfigStore.LoadAsync(CancellationToken) -> ValueTask<AppConfig>`
- `IAppConfigStore.SaveAsync(AppConfig, CancellationToken) -> ValueTask`
- `SettingsViewModel.WorkingCopy : AppConfig`
- `SettingsViewModel.HasUnsavedChanges : bool`
- `SettingsViewModel.RequiresModelReload : bool`
- `SettingsViewModel.SaveAsync`, `ResetAsync`, `RestoreDefaults`.

**Reload-required fields:** `ModelRepo`, `ModelFile`, `BackendPreference`, `MaxTokens`, `Temperature`, `GpuLayers`.

**Live-applied fields in this slice:** `TranslateEnabled`, `RewriteEnabled`, `TargetLanguage`, `FavoriteLanguages`, `RewriteSystemPrompt`, `TranslateSystemPrompt`, `ResultAction`, `HistoryRetentionDays`, `PopoverHideSeconds`.

- [ ] Write failing tests using an in-memory fake store: constructor/load creates a detached working copy; editing does not save automatically; `ResetAsync` restores persisted values; `RestoreDefaults` copies `AppConfig.Default` but does not save; `SaveAsync` persists once; changing only `TargetLanguage` does not require reload; changing `ModelFile` or `BackendPreference` does require reload.
- [ ] Confirm RED.
- [ ] Implement `SettingsChangePolicy.RequiresModelReload(AppConfig persisted, AppConfig candidate)` comparing exactly the reload-required fields above.
- [ ] Implement `SettingsViewModel` so save validates target language via `AppConfig.NormalizeTargetLanguage`, persists through `IAppConfigStore`, updates persisted baseline, and retains `RequiresModelReload=true` when a reload-required change was saved until a later reload action clears it.
- [ ] Adapt existing `JsonConfigStore` through `JsonAppConfigStoreAdapter`; do not move JSON serialization into App.
- [ ] Run tests and confirm GREEN.
- [ ] Commit `feat: add settings save and reload policy`.

### Task 4: Settings page UI and Slice 1 verification

**Files:**
- Create: `src/Butchi.App/Settings/SettingsView.cs`
- Modify: `src/Butchi.App/Management/ManagementWindow.cs`
- Modify: `src/Butchi.App/Composition/ButchiServiceCollectionExtensions.cs`
- Test: `tests/Butchi.App.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes `SettingsViewModel` and management page selection.
- Produces typed controls for existing `AppConfig` fields and visible reload-required state.

- [ ] Add/extend headless tests for save/reset/default command state and reload-required indicator; do not require a visible desktop session.
- [ ] Implement the thin Avalonia settings page with typed enum controls for `BackendPreference`/`ResultAction`, text/language/prompt fields, numeric inference fields, Save/Reset/Restore Defaults buttons, and a visible `Reload model required` status when applicable.
- [ ] Wire Settings as the real content for `ManagementPage.Settings`; other pages remain placeholders for later Task 10 slices.
- [ ] Run `dotnet test Butchi.slnx -c Release` and the existing win-x64/win-arm64 publish-smoke workflow.
- [ ] Inspect review comments and keep this PR limited to Slice 1.
- [ ] Mark ready only when CI is green; merge only after user approval.

---

## Slice 2 — History page

### Task 5: History orchestration and page

**Files:**
- Create: `src/Butchi.App/History/IHistoryService.cs`
- Create: `src/Butchi.App/History/SqliteHistoryServiceAdapter.cs`
- Create: `src/Butchi.App/History/HistoryViewModel.cs`
- Create: `src/Butchi.App/History/HistoryView.cs`
- Modify: `src/Butchi.App/Management/ManagementWindow.cs`
- Test: `tests/Butchi.App.Tests/HistoryViewModelTests.cs`

**Interfaces:**
- Query recent/search/filter/limit through existing history-store capabilities.
- Delete one and clear all via service adapter.

- [ ] TDD search/filter/refresh/delete/clear orchestration using fakes.
- [ ] Keep entry text out of logs.
- [ ] Add thin Avalonia list/search/filter/delete/clear/copy UI.
- [ ] Run full tests/publish smokes; merge Slice 2 only when green and user-approved.

---

## Slice 3 — Models, first run, status

### Task 6: Model/status presentation and first-run routing

**Files:**
- Create: `src/Butchi.App/Models/ModelManagerViewModel.cs`
- Create: `src/Butchi.App/Models/ModelManagerView.cs`
- Create: `src/Butchi.App/Status/StatusViewModel.cs`
- Create: `src/Butchi.App/Status/StatusView.cs`
- Create: `src/Butchi.App/Startup/FirstRunPolicy.cs`
- Modify: `src/Butchi.App/Management/ManagementWindow.cs`
- Test: `tests/Butchi.App.Tests/ModelManagerViewModelTests.cs`
- Test: `tests/Butchi.App.Tests/FirstRunPolicyTests.cs`

- [ ] TDD model state mapping: not-downloaded/downloading/downloaded-unloaded/loading/loaded/error.
- [ ] TDD download/cancel/load/unload-before-delete commands using existing model services.
- [ ] TDD first-run routing to Models when no usable model is available.
- [ ] TDD status uses actual `IInferenceEngine.Status` backend/device rather than desired config alone.
- [ ] Implement thin views and startup routing.
- [ ] Run full tests/publish smokes; merge Slice 3 only when green and user-approved.

---

## Slice 4 — Tray and Task 10 final integration

### Task 7: Tray routing and theme propagation

**Files:**
- Create: `src/Butchi.App/Tray/TrayCommandRouter.cs`
- Create: `src/Butchi.App/Theme/AppThemeService.cs`
- Modify: `src/Butchi.App/App.cs`
- Modify: `src/Butchi.App/Management/ManagementWindow.cs`
- Test: `tests/Butchi.App.Tests/TrayCommandRouterTests.cs`
- Test: `tests/Butchi.App.Tests/AppThemeServiceTests.cs`

- [ ] TDD tray commands route Settings/History/Models to the same reusable management window and Exit to application shutdown.
- [ ] TDD System/Light/Dark theme state propagates to both popover and management windows.
- [ ] Implement native Avalonia tray menu/lifecycle without redesigning Task 9 trigger semantics.
- [ ] Run full tests and publish smokes.
- [ ] Verify all Task 10 requirements from the spec, resolve review comments, and merge final slice only after user approval.
