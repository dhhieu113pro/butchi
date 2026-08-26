# Native Avalonia Popover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persistent, efficient native Avalonia popover with isolated Translate/Rewrite streaming state and tested geometry/interaction policies.

**Architecture:** `Butchi.App` owns the Avalonia view, view model, and pure geometry policies. A single `PopoverWindow` is reused across actions; presentation updates are keyed by scheduler run IDs and batched before notifying the UI.

**Tech Stack:** .NET 10, Avalonia Desktop, xUnit

**Spec:** `docs/superpowers/specs/2026-08-25-native-avalonia-popover-design.md`

## Global Constraints
- Keep Avalonia dependencies out of `Butchi.Core`.
- Reuse one popover window instance.
- Reject stale run updates.
- Do not add Win32 hooks/selection acquisition in Task 8.
- Preserve win-x64 and win-arm64 publish smoke coverage.

---

### Task 1: Presentation state and streaming batching

**Files:**
- Create: `src/Butchi.App/Popover/PopoverViewModel.cs`
- Create: `src/Butchi.App/Popover/ActionPresentationState.cs`
- Test: `tests/Butchi.App.Tests/PopoverViewModelTests.cs`

**Interfaces:**
- Produces: run-aware Translate/Rewrite presentation state and batched stream update methods.

- [ ] Write failing tests for independent action state, stale run rejection, and batching.
- [ ] Run tests and confirm RED for missing presentation types.
- [ ] Implement minimum state/view-model behavior.
- [ ] Run tests and confirm GREEN.
- [ ] Commit.

### Task 2: Popover geometry policies

**Files:**
- Create: `src/Butchi.App/Popover/PopoverGeometry.cs`
- Test: `tests/Butchi.App.Tests/PopoverGeometryTests.cs`

**Interfaces:**
- Produces: bounded size calculation and cursor-adjacent placement clamped to a supplied working area.

- [ ] Write failing sizing and placement tests.
- [ ] Confirm RED.
- [ ] Implement pure geometry policy.
- [ ] Confirm GREEN.
- [ ] Commit.

### Task 3: Interaction policy

**Files:**
- Modify: `src/Butchi.App/Popover/PopoverViewModel.cs`
- Test: `tests/Butchi.App.Tests/PopoverViewModelTests.cs`

**Interfaces:**
- Produces: favorite-language translate request event/command and configurable auto-hide state.

- [ ] Write failing favorite-language and auto-hide tests.
- [ ] Confirm RED.
- [ ] Implement minimum interaction behavior.
- [ ] Confirm GREEN.
- [ ] Commit.

### Task 4: Persistent Avalonia window

**Files:**
- Create: `src/Butchi.App/Popover/PopoverWindow.cs`
- Modify: `src/Butchi.App/App.cs`
- Modify: `src/Butchi.App/Program.cs` only if startup wiring requires it.
- Test: `tests/Butchi.App.Tests/PopoverWindowPolicyTests.cs` where behavior can be tested without a visible desktop.

**Interfaces:**
- Consumes: `PopoverViewModel`, `PopoverGeometry`.
- Produces: one reusable borderless/topmost/taskbar-hidden window with Escape-to-hide, bounded scrolling, and theme variant application.

- [ ] Write headless policy tests that do not require a visible Windows session.
- [ ] Confirm RED.
- [ ] Implement persistent window and bindings.
- [ ] Confirm app tests GREEN.
- [ ] Run full solution tests.
- [ ] Run/preserve win-x64 and win-arm64 publish smoke checks.
- [ ] Commit.

### Task 5: PR verification

- [ ] Verify full CI is green.
- [ ] Inspect and address review comments.
- [ ] Ensure PR contains Task 8 only.
- [ ] Mark ready for review; merge only after user approval.