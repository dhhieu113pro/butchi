# Butchi Settings UI/UX Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Butchi's long Settings card stack with a navigable, auto-saving desktop settings experience while preserving all existing functionality and the Hallmark visual system.

**Architecture:** Keep the existing single Tauri Settings window and existing persistence/backend APIs. Restructure `settings.html` into five semantic panels controlled by a small navigation state in `src/settings.ts`; CSS provides a desktop rail and narrow-width top tabs. Existing setting controls keep their IDs where possible to minimize behavioral regression, while save orchestration changes from explicit footer save to debounced auto-save.

**Tech Stack:** Tauri 2, Vite, vanilla TypeScript, HTML/CSS, existing Vitest frontend tests, Rust/Tauri backend.

**Spec:** `docs/superpowers/specs/2026-08-23-settings-ui-ux-redesign-design.md`

## Global Constraints

- Preserve Cobalt design tokens, light/dark/system themes, 4pt spacing, and restrained/reduced motion behavior.
- Keep the existing Settings window; do not create additional native windows.
- Preserve existing setting IDs and persistence semantics wherever practical.
- Five destinations only: General, Prompts, Model, History, About & Privacy.
- Desktop uses a left navigation rail; narrow widths use a horizontally scrollable top tab row.
- Settings auto-save; destructive actions remain explicit.
- Advanced inference controls are progressively disclosed.
- Popover changes are limited to redundant success/rerun affordances and must not become a redesign.

---

### Task 1: Settings navigation shell

**Files:**
- Modify: `settings.html`
- Modify: `src/settings.css`
- Modify: `src/settings.ts`
- Test: existing frontend settings test file(s) discovered under `src/**/*.test.ts`

**Interfaces:**
- Produces: navigation buttons with `data-settings-page`, panels with matching `data-settings-panel`, and `setSettingsPage(page: SettingsPage): void` in `src/settings.ts`.
- `SettingsPage` is the union `"general" | "prompts" | "model" | "history" | "about"`.

- [ ] **Step 1: Locate current settings tests and add failing navigation tests**

Add tests that mount the Settings DOM, assert General is selected initially, activate History, and assert `aria-selected`/panel visibility move together. Also assert every navigation button references an existing panel.

- [ ] **Step 2: Run the focused frontend test and verify failure**

Run the repository's existing Vitest command targeting the settings test file. Expected: FAIL because the navigation shell and page state do not exist.

- [ ] **Step 3: Restructure the HTML into the five approved panels**

Use this semantic shape while retaining existing control IDs inside their destination:

```html
<div class="settings-shell">
  <nav class="settings-nav" aria-label="Settings sections">
    <button type="button" data-settings-page="general" aria-selected="true" aria-controls="settings-general">General</button>
    <button type="button" data-settings-page="prompts" aria-selected="false" aria-controls="settings-prompts">Prompts</button>
    <button type="button" data-settings-page="model" aria-selected="false" aria-controls="settings-model">Model</button>
    <button type="button" data-settings-page="history" aria-selected="false" aria-controls="settings-history">History</button>
    <button type="button" data-settings-page="about" aria-selected="false" aria-controls="settings-about">About &amp; Privacy</button>
  </nav>
  <div class="settings-content">
    <section id="settings-general" data-settings-panel="general"></section>
    <section id="settings-prompts" data-settings-panel="prompts" hidden></section>
    <section id="settings-model" data-settings-panel="model" hidden></section>
    <section id="settings-history" data-settings-panel="history" hidden></section>
    <section id="settings-about" data-settings-panel="about" hidden></section>
  </div>
</div>
```

- [ ] **Step 4: Implement navigation state in TypeScript**

```ts
type SettingsPage = "general" | "prompts" | "model" | "history" | "about";

function setSettingsPage(page: SettingsPage): void {
  document.querySelectorAll<HTMLElement>("[data-settings-panel]").forEach((panel) => {
    panel.hidden = panel.dataset.settingsPanel !== page;
  });
  document.querySelectorAll<HTMLButtonElement>("[data-settings-page]").forEach((button) => {
    button.setAttribute("aria-selected", String(button.dataset.settingsPage === page));
  });
}
```

Wire each navigation button click to `setSettingsPage` and initialize General.

- [ ] **Step 5: Implement responsive shell styling**

Use a two-column grid at normal widths, a compact sticky/visible rail, and at the existing narrow breakpoint switch `.settings-shell` to one column with `.settings-nav` as a horizontal `overflow-x: auto` row. Add explicit `:focus-visible` rings using `var(--color-focus)`.

- [ ] **Step 6: Run focused tests and frontend build**

Expected: navigation tests PASS and `npm run build` succeeds.

- [ ] **Step 7: Commit**

```bash
git add settings.html src/settings.css src/settings.ts src/**/*.test.ts
git commit -m "feat: add settings navigation shell"
```

### Task 2: General and Prompts information architecture

**Files:**
- Modify: `settings.html`
- Modify: `src/settings.css`
- Modify: `src/settings.ts`
- Test: settings frontend test file(s)

**Interfaces:**
- Produces: prompt mode buttons `data-prompt-mode="translate|rewrite"`, panels `data-prompt-panel`, and `setPromptMode(mode: "translate" | "rewrite"): void`.
- Consumes existing control IDs for theme, action toggles, languages, result action, prompt profiles, and prompt textareas.

- [ ] **Step 1: Add failing tests for destination ownership and prompt switching**

Assert General contains theme/action/language/result controls and Prompts contains both existing prompt profile/textarea controls. Assert Translate is initially selected, switching to Rewrite updates `aria-selected` and visible prompt panel, and existing profile-to-custom behavior still works.

- [ ] **Step 2: Run focused tests and verify failure**

Expected: FAIL because prompt segmented navigation does not exist.

- [ ] **Step 3: Move existing controls without changing IDs**

General contains Appearance + Actions content. Prompts uses one header and this mode control:

```html
<div class="segmented" role="tablist" aria-label="Prompt type">
  <button type="button" role="tab" data-prompt-mode="translate" aria-selected="true" aria-controls="prompt-translate">Translate</button>
  <button type="button" role="tab" data-prompt-mode="rewrite" aria-selected="false" aria-controls="prompt-rewrite">Rewrite</button>
</div>
```

Keep `translatePromptProfile`, `translateSystemPrompt`, `rewritePromptProfile`, and `rewriteSystemPrompt` unchanged.

- [ ] **Step 4: Implement `setPromptMode`**

Mirror the settings-page state pattern and keep the existing profile synchronization logic intact.

- [ ] **Step 5: Style the segmented control and simplify card nesting**

Avoid a card inside every destination. Use section headings, grouped fields, and subtle rules/spacing; reserve raised cards for model setup/status or destructive content that needs separation.

- [ ] **Step 6: Run tests/build**

Expected: focused tests and `npm run build` PASS.

- [ ] **Step 7: Commit**

```bash
git add settings.html src/settings.css src/settings.ts src/**/*.test.ts
git commit -m "feat: reorganize general and prompt settings"
```

### Task 3: Model workflow and progressive disclosure

**Files:**
- Modify: `settings.html`
- Modify: `src/settings.css`
- Modify: `src/settings.ts`
- Test: settings/model frontend test file(s)

**Interfaces:**
- Produces: `#modelSetup`, `#modelReadySummary`, `#modelAdvanced`, and an Advanced button with `aria-expanded`/`aria-controls`.
- Consumes existing `modelSelect`, `modelStatus`, `downloadBtn`, `loadBtn`, `backendPreference`, `backendPills`, `deviceList`, `maxTokens`, `temperature`, and `gpuLayers`.

- [ ] **Step 1: Add failing model-state and disclosure tests**

Test that setup state exposes Download/Load and model status, ready state can show a compact summary, and Advanced starts collapsed and toggles `aria-expanded` plus content visibility. Preserve tests for model/backend behavior.

- [ ] **Step 2: Run focused tests and verify failure**

Expected: FAIL because the new state containers/disclosure do not exist.

- [ ] **Step 3: Recompose Model destination**

Put model picker/status/download/load first. Keep Device preference immediately visible. Move backend diagnostics and tuning controls into:

```html
<button type="button" class="disclosure" id="modelAdvancedToggle" aria-expanded="false" aria-controls="modelAdvanced">Advanced</button>
<div id="modelAdvanced" hidden>
  <!-- backend pills/device list/max tokens/temperature/gpu layers -->
</div>
```

- [ ] **Step 4: Wire disclosure and model setup/ready presentation**

Reuse existing model status data rather than adding a new backend API. Presentation state must derive from the same loaded/downloaded status already used by Settings.

- [ ] **Step 5: Style model setup as the strongest empty-state surface**

Use one raised panel for the recommended model/setup path; keep ready state compact. Add keyboard focus and no-overflow rules.

- [ ] **Step 6: Run model/settings tests and frontend build**

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add settings.html src/settings.css src/settings.ts src/**/*.test.ts
git commit -m "feat: improve local model settings flow"
```

### Task 4: First-class History and About & Privacy

**Files:**
- Modify: `settings.html`
- Modify: `src/settings.css`
- Modify: `src/settings.ts` only if selectors depend on moved markup
- Test: history/settings frontend test file(s)

**Interfaces:**
- Preserves existing History control IDs and history rendering functions.
- Preserves `clearLocalDataBtn` and `appVersion` behavior.

- [ ] **Step 1: Add failing structural regression tests**

Assert History controls are inside the History destination and not General/Model. Assert privacy/local-data cleanup and version/license/support content are in About & Privacy. Existing history search/filter/retention tests must continue to pass.

- [ ] **Step 2: Run focused tests and verify failure**

Expected: FAIL against the old card placement.

- [ ] **Step 3: Move History unchanged behaviorally**

Keep Search + action filter first, history list next, retention visually secondary, and Refresh/Clear accessible. Preserve the local-only explanation.

- [ ] **Step 4: Compose About & Privacy**

Put privacy explanation first, network-download explanation second, then a separated danger/local-data area containing `clearLocalDataBtn`. Put version, MIT license, and project/support details last.

- [ ] **Step 5: Run history/settings tests and build**

Expected: PASS with no selector regressions.

- [ ] **Step 6: Commit**

```bash
git add settings.html src/settings.css src/settings.ts src/**/*.test.ts
git commit -m "feat: promote history and privacy settings"
```

### Task 5: Replace explicit Save with debounced auto-save

**Files:**
- Modify: `settings.html`
- Modify: `src/settings.ts`
- Modify: `src/settings.css`
- Test: settings persistence frontend test file(s)

**Interfaces:**
- Produces: `scheduleSettingsSave(): void` and status element `#saveStatus` with polite live-region behavior.
- Consumes the existing save/persist function currently invoked by `#saveBtn`; do not duplicate persistence logic.

- [ ] **Step 1: Add failing auto-save tests**

Use fake timers. Change a persisted control, assert no immediate write before debounce, advance approximately 300 ms, assert the existing persistence function runs once, and assert status progresses through `Saving…` to `Saved`. Add an error test asserting a persistence rejection yields a visible error and does not report Saved.

- [ ] **Step 2: Run focused tests and verify failure**

Expected: FAIL because settings only save from the explicit button.

- [ ] **Step 3: Extract/reuse the current save operation**

Keep one async persistence function. Implement a debounce around it:

```ts
let saveTimer: number | undefined;

function scheduleSettingsSave(): void {
  window.clearTimeout(saveTimer);
  saveTimer = window.setTimeout(() => void persistSettingsWithStatus(), 300);
}
```

Wire `change` for selects/checkboxes/numbers and debounced `input` for prompt textareas to this scheduler. Do not auto-run destructive actions.

- [ ] **Step 4: Remove the Save button and retain status feedback**

Remove `#saveBtn`. Place `#saveStatus` unobtrusively in the settings header/content shell, using `aria-live="polite"`. Clear `Saved` after a short delay so success chrome does not remain permanently.

- [ ] **Step 5: Run persistence/settings tests and frontend build**

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add settings.html src/settings.ts src/settings.css src/**/*.test.ts
git commit -m "feat: auto save settings changes"
```

### Task 6: Hallmark interaction-state and popover polish

**Files:**
- Modify: `src/settings.css`
- Modify: `index.html` only if rerun copy is adopted
- Modify: `src/main.ts` only if success state text is controlled there
- Modify: `src/popover.css` only as needed
- Test: popover/settings frontend test file(s)

**Interfaces:**
- Preserves all existing Translate/Rewrite action behavior.
- Removes redundant visible success text only; loading/error remain explicit.

- [ ] **Step 1: Add failing accessibility/state tests**

Assert settings nav/tabs/disclosure have correct ARIA state after interaction. For popover, assert successful visible results do not need a persistent `Done` label, while loading and error labels remain.

- [ ] **Step 2: Run focused tests and verify failure where behavior changes**

Expected: popover success-state assertion fails against current `Done` behavior.

- [ ] **Step 3: Complete focus/hover/active/disabled styling**

Add explicit `:focus-visible` treatment to `.settings-nav button`, `.segmented button`, `.btn`, `.disclosure`, inputs, selects, and textareas using the existing focus token. Ensure active navigation also has a structural marker such as border/inset indicator, not color alone.

- [ ] **Step 4: Remove redundant popover success text**

When a result is visible and successful, leave the state label empty instead of `Done`. Keep `Generating…`/loading and error text. Only rename the bottom actions to rerun wording if the resulting compact layout remains clean; otherwise leave button labels unchanged.

- [ ] **Step 5: Run all frontend tests/build**

Run the repository's full frontend test command and `npm run build`. Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add settings.html index.html src/settings.css src/settings.ts src/main.ts src/popover.css src/**/*.test.ts
git commit -m "fix: complete settings interaction states"
```

### Task 7: Full repository verification

**Files:**
- Modify only if verification reveals a regression.

**Interfaces:**
- No new interfaces; this task proves the redesigned Settings remains compatible with the existing application and CI.

- [ ] **Step 1: Run formatting/linting commands used by CI**

Run the frontend formatting/lint command if present in `package.json`, then Rust formatting and Clippy commands exactly as defined in `.github/workflows`.

- [ ] **Step 2: Run the full frontend test suite**

Run the package test command. Expected: all tests PASS.

- [ ] **Step 3: Run frontend production build**

Run `npm run build`. Expected: PASS.

- [ ] **Step 4: Run Rust check/tests for the same features CI exercises**

Run the `cargo check`, `cargo test`, and strict `cargo clippy ... -- -D warnings` variants used by the current workflow. Expected: PASS.

- [ ] **Step 5: Review responsive/accessibility invariants**

Verify no narrow-width horizontal page overflow, keyboard focus is visible, every tab/nav/disclosure state has matching ARIA state, destructive controls remain explicit, and auto-save never fires destructive operations.

- [ ] **Step 6: Final commit only if verification required fixes**

Use a focused message describing the actual verification fix; do not create an empty/no-op commit.
