# Windows Platform Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Windows selection, pointer context, and double-Ctrl trigger integration for the persistent Butchi popover.

**Architecture:** All Win32 and UI Automation details live in `Butchi.Platform.Windows` behind neutral interfaces. `Butchi.App` only coordinates trigger → selection → pointer context → existing `PopoverGeometry` → persistent `PopoverWindow`.

**Tech Stack:** .NET 10, Avalonia Desktop, Windows P/Invoke/UI Automation, xUnit

**Spec:** `docs/superpowers/specs/2026-08-25-windows-platform-integration-design.md`

## Global Constraints
- Keep Win32 types out of `Butchi.Core`.
- Keep platform implementation in `Butchi.Platform.Windows`.
- Default trigger is double Ctrl within 350 ms.
- Ignore modifier combinations and repeat noise.
- Preserve clipboard contents around Ctrl+C fallback.
- Reuse the existing persistent popover window.
- Preserve win-x64 and win-arm64 publish smoke checks.

---

### Task 1: Double-Ctrl trigger policy

**Files:**
- Create: `src/Butchi.Platform.Windows/Triggers/DoubleCtrlDetector.cs`
- Test: `tests/Butchi.Platform.Windows.Tests/DoubleCtrlDetectorTests.cs`

- [ ] Write failing timing/modifier/repeat tests.
- [ ] Confirm RED.
- [ ] Implement pure detector.
- [ ] Confirm GREEN.
- [ ] Commit.

### Task 2: Selection acquisition policy

**Files:**
- Create: `src/Butchi.Platform.Windows/Selection/ISelectionSource.cs`
- Create: `src/Butchi.Platform.Windows/Selection/WindowsSelectionReader.cs`
- Test: `tests/Butchi.Platform.Windows.Tests/WindowsSelectionReaderTests.cs`

- [ ] Write failing UIA-first/fallback/restore tests.
- [ ] Confirm RED.
- [ ] Implement policy against fakes.
- [ ] Confirm GREEN.
- [ ] Commit.

### Task 3: Pointer context

**Files:**
- Create: `src/Butchi.Platform.Windows/Pointer/PointerContext.cs`
- Create: `src/Butchi.Platform.Windows/Pointer/WindowsPointerContext.cs`
- Test: `tests/Butchi.Platform.Windows.Tests/WindowsPointerContextTests.cs`

- [ ] Write failing mapping tests against a Win32 adapter.
- [ ] Confirm RED.
- [ ] Implement adapter-backed pointer context.
- [ ] Confirm GREEN.
- [ ] Commit.

### Task 4: Native adapters and trigger service

**Files:**
- Create: `src/Butchi.Platform.Windows/Interop/NativeMethods.cs`
- Create: `src/Butchi.Platform.Windows/Triggers/WindowsTriggerService.cs`
- Create UIA/clipboard adapters under `Selection/`.
- Test lifecycle with injectable adapters.

- [ ] Write failing lifecycle tests.
- [ ] Confirm RED.
- [ ] Add minimal P/Invoke/UIA/clipboard implementation.
- [ ] Confirm GREEN.
- [ ] Commit.

### Task 5: App orchestration

**Files:**
- Create: `src/Butchi.App/Windows/WindowsPopoverCoordinator.cs`
- Modify: `src/Butchi.App/App.cs`
- Test: `tests/Butchi.App.Tests/WindowsPopoverCoordinatorTests.cs`

- [ ] Write failing trigger/no-selection/placement/show tests with fakes.
- [ ] Confirm RED.
- [ ] Implement coordinator and startup wiring.
- [ ] Confirm GREEN.
- [ ] Run full solution tests and publish smoke checks.
- [ ] Commit.

### Task 6: PR verification

- [ ] Verify CI green.
- [ ] Resolve review comments.
- [ ] Ensure Task 9-only scope.
- [ ] Mark ready for review; merge only after user approval.
