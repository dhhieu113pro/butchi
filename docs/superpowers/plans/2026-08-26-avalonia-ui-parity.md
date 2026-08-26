# Task 14 Avalonia UI Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recreate the established Butchi Tauri UI/UX as native .NET 10/Avalonia surfaces with real functionality and screenshot evidence.

**Architecture:** Keep the existing Core/Infrastructure/Inference/Platform services and replace placeholder Avalonia presentation incrementally. Shared theme resources and focused view models provide the visual/behavioral contract; each page binds to existing services rather than duplicating business logic.

**Tech Stack:** .NET 10, Avalonia, xUnit, existing Butchi application services, GitHub Actions screenshot smoke.

**Spec:** `docs/superpowers/specs/2026-08-26-avalonia-ui-parity-design.md`

## Global Constraints

- `legacy-tauri` is the visual/information-architecture reference.
- Native Avalonia only; no Tauri/WebView/HTML runtime dependencies.
- Preserve existing inference, settings, history, model, packaging, and release behavior.
- Support Light, Dark, and System themes.
- Keep screenshot CI: natural-size popover and 1440×900 management captures.
- Every implementation slice uses TDD and must provide screenshot evidence before merge.

---

### Task 1: Shared design system, Settings shell, and General

**Files:**
- Modify: `src/Butchi.App/App.axaml`
- Modify: `src/Butchi.App/Views/ManagementWindow.axaml`
- Modify: `src/Butchi.App/Views/ManagementWindow.axaml.cs`
- Create: `src/Butchi.App/Styles/ButchiTheme.axaml`
- Create: `src/Butchi.App/ViewModels/GeneralSettingsViewModel.cs`
- Test: `tests/Butchi.App.Tests/ManagementWindowTests.cs`
- Test: `tests/Butchi.App.Tests/GeneralSettingsViewModelTests.cs`

**Interfaces:**
- Consumes: existing settings persistence/application services already used by `Butchi.App`.
- Produces: `GeneralSettingsViewModel` with Theme, TranslateEnabled, RewriteEnabled, TargetLanguage, FavoriteLanguages, ResultAction, PopoverHideSeconds, and SaveStatus; shared `ButchiTheme.axaml` resources used by later views.

- [ ] **Step 1: Write failing tests** asserting the management shell exposes General/Prompts/Model/History/About destinations and `GeneralSettingsViewModel` loads/saves the existing settings fields without an explicit Save command.
- [ ] **Step 2: Run** `dotnet test tests/Butchi.App.Tests/Butchi.App.Tests.csproj -c Release` and verify the new tests fail for missing shell/resources/view model.
- [ ] **Step 3: Add `ButchiTheme.axaml`** with Light/Dark/System-compatible background, surface, text, muted, border, cobalt accent, focus, state, radius, spacing, button, input, card, nav, segmented, pill, and disclosure resources; include it from `App.axaml`.
- [ ] **Step 4: Replace the placeholder management shell** with branded left navigation, useful content width, page header, scrollable content, and explicit selected/focus states. Make General the default destination.
- [ ] **Step 5: Implement General** with Appearance and Actions sections and bind all approved fields to `GeneralSettingsViewModel`; persist changes through the existing settings service and expose Saving/Saved/error status.
- [ ] **Step 6: Run the focused tests**, then `dotnet test Butchi.slnx -c Release` and verify PASS.
- [ ] **Step 7: Run screenshot mode** for Settings/General at 1440×900 and verify the image contains the branded shell and populated controls rather than a title-only page.
- [ ] **Step 8: Commit** `feat: restore Avalonia settings shell and general UI`.

### Task 2: Prompts page

**Files:**
- Create: `src/Butchi.App/ViewModels/PromptsViewModel.cs`
- Create: `src/Butchi.App/Views/PromptsView.axaml`
- Create: `src/Butchi.App/Views/PromptsView.axaml.cs`
- Modify: `src/Butchi.App/Views/ManagementWindow.axaml`
- Test: `tests/Butchi.App.Tests/PromptsViewModelTests.cs`

**Interfaces:**
- Consumes: existing persisted Translate/Rewrite prompt profile and system-prompt settings.
- Produces: `PromptsViewModel` with `Mode`, profile collections, selected profiles, prompt text, and automatic Custom transition on edited preset text.

- [ ] **Step 1: Write failing tests** for Translate/Rewrite mode switching, profile loading, and preset-text edit → Custom behavior.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Implement `PromptsViewModel`** against existing settings persistence.
- [ ] **Step 4: Implement the native Prompts view** with a segmented Translate/Rewrite selector, profile field, large system-prompt editor, hint copy, and shared theme styles.
- [ ] **Step 5: Wire Prompts navigation** into the management shell without opening a second window.
- [ ] **Step 6: Run focused/full tests** and verify PASS.
- [ ] **Step 7: Capture/review a 1440×900 Prompts screenshot** and confirm both hierarchy and editor density match the legacy reference direction.
- [ ] **Step 8: Commit** `feat: restore Avalonia prompts UI`.

### Task 3: Model page and states

**Files:**
- Create: `src/Butchi.App/ViewModels/ModelManagementViewModel.cs`
- Create: `src/Butchi.App/Views/ModelManagementView.axaml`
- Create: `src/Butchi.App/Views/ModelManagementView.axaml.cs`
- Modify: `src/Butchi.App/Views/ManagementWindow.axaml`
- Test: `tests/Butchi.App.Tests/ModelManagementViewModelTests.cs`

**Interfaces:**
- Consumes: existing model catalog/download/load/backend/status services.
- Produces: model setup/ready state, selected model, download/load commands, backend preference, active/available backends, max tokens, temperature, GPU layers, and Advanced expansion state.

- [ ] **Step 1: Write failing tests** for no-model setup state, ready state, download/load command delegation, backend preference, and Advanced fields.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Implement the view model** by adapting existing model services; do not duplicate inference logic.
- [ ] **Step 4: Build the Model view** with recommended setup prominence, model/status controls, Device section, backend status pills, and collapsed Advanced inference settings.
- [ ] **Step 5: Wire Model navigation** into the management shell.
- [ ] **Step 6: Run focused/full tests and x64/ARM64 publish checks**; verify PASS.
- [ ] **Step 7: Capture/review model setup and ready screenshots** using deterministic screenshot fixtures.
- [ ] **Step 8: Commit** `feat: restore Avalonia model management UI`.

### Task 4: History page and states

**Files:**
- Create: `src/Butchi.App/ViewModels/HistoryViewModel.cs`
- Create: `src/Butchi.App/Views/HistoryView.axaml`
- Create: `src/Butchi.App/Views/HistoryView.axaml.cs`
- Modify: `src/Butchi.App/Views/ManagementWindow.axaml`
- Test: `tests/Butchi.App.Tests/HistoryViewModelTests.cs`

**Interfaces:**
- Consumes: existing history repository/application service.
- Produces: query, action filter, entries, retention, loading/error/empty states, Refresh/Clear/Copy/Delete commands.

- [ ] **Step 1: Write failing tests** for search/filter composition, empty/loading/error state, refresh, clear, entry delete/copy delegation, and retention persistence.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Implement `HistoryViewModel`** against the existing local history service.
- [ ] **Step 4: Build History UI** with header actions, search/filter row, designed entry cards, empty/loading/error states, and visually secondary retention settings.
- [ ] **Step 5: Wire History navigation** into the management shell.
- [ ] **Step 6: Run focused/full tests** and verify PASS.
- [ ] **Step 7: Capture/review populated and empty History screenshots** at 1440×900.
- [ ] **Step 8: Commit** `feat: restore Avalonia history UI`.

### Task 5: About & Privacy and status

**Files:**
- Create: `src/Butchi.App/ViewModels/AboutPrivacyViewModel.cs`
- Create: `src/Butchi.App/Views/AboutPrivacyView.axaml`
- Create: `src/Butchi.App/Views/AboutPrivacyView.axaml.cs`
- Modify: `src/Butchi.App/Views/ManagementWindow.axaml`
- Test: `tests/Butchi.App.Tests/AboutPrivacyViewModelTests.cs`

**Interfaces:**
- Consumes: app version/status plus existing history/model cleanup services.
- Produces: version/project/license/status display and `DeleteLocalDataCommand` that clears history + downloaded models while retaining settings.

- [ ] **Step 1: Write failing tests** for displayed metadata and destructive cleanup boundaries.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Implement the view model** and cleanup delegation.
- [ ] **Step 4: Build About & Privacy** with local-processing/network explanation, version/license/project information, status summary, and separated danger zone.
- [ ] **Step 5: Wire About navigation** and map the existing status screenshot target to this useful status surface.
- [ ] **Step 6: Run focused/full tests** and verify PASS.
- [ ] **Step 7: Capture/review the 1440×900 About/Status screenshot**.
- [ ] **Step 8: Commit** `feat: restore Avalonia about privacy UI`.

### Task 6: Native popover parity

**Files:**
- Modify: `src/Butchi.App/Views/PopoverWindow.axaml`
- Modify: `src/Butchi.App/Views/PopoverWindow.axaml.cs`
- Modify/Create: focused popover view model files under `src/Butchi.App/ViewModels/`
- Test: `tests/Butchi.App.Tests/PopoverWindowTests.cs`
- Test: focused popover view-model tests under `tests/Butchi.App.Tests/`

**Interfaces:**
- Consumes: existing Translate/Rewrite application commands, language settings, clipboard/replacement behavior, and popover lifecycle.
- Produces: compact native action/source/result UI with idle/loading/success/error states and result actions.

- [ ] **Step 1: Write failing tests** for Translate/Rewrite selection, loading, result, error, target-language selection, rerun/copy/replace actions, and auto-hide lifecycle.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Implement/refine the popover view model** using existing application services.
- [ ] **Step 4: Rebuild `PopoverWindow.axaml`** using shared Cobalt resources, canonical logo, compact action selector, source/result surfaces, language affordances, state messaging, and result actions.
- [ ] **Step 5: Preserve natural popover sizing** and tray/selection-trigger behavior.
- [ ] **Step 6: Run focused/full tests** and verify PASS.
- [ ] **Step 7: Capture/review natural-size idle, loading, success, and error popover screenshots**.
- [ ] **Step 8: Commit** `feat: restore Avalonia popover UI`.

### Task 7: Final visual parity hardening

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: screenshot-mode files under `src/Butchi.App/`
- Modify: `tests/Butchi.App.Tests/ScreenshotCiContractTests.cs`
- Create: `tests/Butchi.App.Tests/UiParityContractTests.cs`

**Interfaces:**
- Consumes: all Task 14 views.
- Produces: deterministic CI evidence for the finished native UI.

- [ ] **Step 1: Write failing parity contracts** requiring all five primary surfaces, non-placeholder content markers, correct screenshot dimensions, and both light/dark deterministic capture support where practical.
- [ ] **Step 2: Run tests** and verify RED.
- [ ] **Step 3: Extend screenshot fixtures/mode** so CI can render representative deterministic content without calling real inference or network downloads.
- [ ] **Step 4: Update CI** to upload the complete visual evidence set with clear artifact names.
- [ ] **Step 5: Run** `dotnet test Butchi.slnx -c Release`, x64 publish, ARM64 publish, and screenshot smoke; verify all PASS.
- [ ] **Step 6: Review every generated PNG** against `legacy-tauri` and the Task 14 spec; reject title-only/blank/overflowing surfaces even if tests pass.
- [ ] **Step 7: Commit** `test: enforce Avalonia UI parity evidence`.
