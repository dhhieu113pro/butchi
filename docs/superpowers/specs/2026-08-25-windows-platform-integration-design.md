# Task 9 — Windows Platform Integration Design

## Goal
Connect Butchi's persistent Avalonia popover to Windows-native selection, pointer, and trigger behavior without leaking Win32 details into Core or App-facing contracts.

## Architecture
- `Butchi.Platform.Windows` owns all Win32/PInvoke, keyboard-hook, clipboard, UI Automation, cursor, and monitor details.
- `Butchi.App` consumes neutral interfaces/events and orchestrates popover positioning/showing.
- `Butchi.Core` remains free of Windows-specific dependencies.

## Selection acquisition
`WindowsSelectionReader` uses this ordered fallback chain:
1. UI Automation selected-text retrieval from the focused control when available.
2. Controlled Ctrl+C clipboard capture when UIA cannot produce selected text.

Clipboard fallback must preserve and restore the prior clipboard content even when capture fails or is cancelled. Empty/no-selection results are returned as no selection rather than exceptions.

## Pointer context
`WindowsPointerContext` returns:
- current cursor X/Y
- working area of the monitor containing that point

Implementation uses Win32 cursor position plus monitor/work-area APIs. `Butchi.App` converts that data into `PopoverGeometry` input.

## Trigger
Use a low-level keyboard hook for the approved default trigger: double Ctrl.
- trigger when Ctrl is pressed twice within 350 ms
- ignore Ctrl presses that participate in another modifier/key combination
- suppress duplicate trigger emission from key-repeat noise
- expose a neutral event to the app layer
- install/uninstall hook deterministically with service lifetime

## App orchestration
On trigger:
1. acquire selected text
2. if none, do not show a new selection-origin result
3. read cursor + working area
4. place the persistent popover through existing `PopoverGeometry`
5. show the already-created `PopoverWindow`

No new window is created per trigger.

## Testing
Headless tests cover:
- double-Ctrl timing and modifier rejection
- key-repeat/duplicate suppression
- selection fallback order
- clipboard restoration on success and failure
- no-selection behavior
- pointer/working-area mapping through adapters
- trigger registration lifecycle
- app orchestration through fakes

## Non-goals
- tray icon/lifecycle
- installer/startup registration
- settings UI for trigger customization
- macOS/Linux platform support
