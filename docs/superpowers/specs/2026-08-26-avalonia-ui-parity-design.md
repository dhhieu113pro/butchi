# Task 14 — Avalonia UI Parity Design

## Goal
Restore the polished Butchi product UI that existed on `legacy-tauri` as native .NET 10/Avalonia views, while preserving the new Avalonia architecture, local inference behavior, packaging, and screenshot CI.

## Reference implementation
`legacy-tauri` is the visual and information-architecture reference. The migration must reproduce the established Butchi experience rather than invent a new product. The approved reference includes the Cobalt/Hallmark visual language, light/dark/system themes, compact technical styling, 4pt spacing, explicit focus states, responsive settings navigation, and the existing compact popover direction.

## Shared visual system
Create native Avalonia resources for the established Butchi design tokens: background/surface/elevated surface, primary and muted text, border, cobalt accent, success/warning/error states, focus treatment, corner radii, spacing, typography, button/input/card styles, navigation styles, segmented controls, pills, and disclosures. Theme resources must support Light, Dark, and System without duplicating page markup.

The Butchi logo restored in Task 13 remains the canonical app/tray/window/package brand asset.

## Settings shell
Replace the placeholder navigation/title shell with a polished desktop settings surface. The normal layout uses a branded left navigation rail and a content pane. Destinations are General, Prompts, Model, History, and About & Privacy. Navigation selection must be visually identifiable without color alone and keyboard focus must be explicit.

The settings window should render naturally at desktop sizes and must not leave the screenshot viewport mostly blank. CI management captures remain 1440×900 and should demonstrate useful content density.

## General
Implement Appearance and Actions sections with Theme (System/Light/Dark), Enable Translate, Enable Rewrite, Target language, up to five favorite target languages, after-action behavior, and popover auto-hide seconds. Existing persistence semantics are preserved. Settings auto-save after changes and expose a small Saving/Saved/error status instead of a Save button.

## Prompts
Provide a Translate/Rewrite segmented control. Each mode exposes its profile selector and editable system prompt. Editing preset text changes the profile to Custom. Preserve the existing prompt profiles and persisted values.

## Model
When no model is available, make recommended model setup/download the dominant state. When a model is ready, show a compact model/status summary. Keep model selection, Download, Load model, device preference, and active status visible. Put available backends, max tokens, temperature, maximum GPU layers, and detailed diagnostics under an Advanced disclosure. Preserve existing inference/model services; this task changes presentation and view-model wiring, not the inference engine.

## History
History is first-class content. Provide search, Translate/Rewrite action filtering, Refresh, Clear all, retention selection, and entry-level copy/delete behavior. Empty/loading/error states must be designed states rather than blank content. History remains local-only.

## About & Privacy
Explain local processing and that network access is used for explicit model downloads. Show version, MIT license, project/support information, and a visually separated destructive action to delete history plus downloaded models while keeping settings.

## Popover
Recreate the compact polished Translate/Rewrite popover as a native Avalonia surface. It must show clear action selection, source/result content, target-language affordances where applicable, loading state, error state, success/result actions, and the canonical Butchi branding. Do not turn the popover into a full settings window. Its screenshot is captured at natural size.

## Responsiveness and accessibility
The settings shell must remain usable when narrowed: navigation may collapse/reflow rather than causing horizontal overflow. Interactive controls need explicit keyboard focus. Selected tabs/navigation must expose state semantically through Avalonia control state and visually without relying only on color. Respect reduced-motion expectations by avoiding required decorative animation.

## Visual regression CI
Keep the existing real Avalonia screenshot mode. CI must capture `popover.png` at natural size plus representative General/Settings, History, Model, and Status/About surfaces at 1440×900. Screenshot contracts must assert the expected surfaces exist. The screenshots are review evidence: a PR is not considered visually complete merely because files were generated.

## Delivery strategy
Deliver Task 14 as small TDD PRs so each slice is independently reviewable and visually inspectable:

1. Shared design system + real Settings shell + General page.
2. Prompts page.
3. Model page and states.
4. History page and states.
5. About & Privacy page and status information.
6. Native popover parity and states.
7. Final screenshot/parity hardening across Light/Dark where practical.

Each slice starts with focused view-model/contract tests, proves RED, implements the minimum native Avalonia UI, runs the full solution tests/publish checks, and uploads screenshot evidence before merge.

## Non-goals
Do not reintroduce Tauri/WebView/HTML runtime dependencies. Do not redesign Butchi into a different product. Do not change inference algorithms, model formats, Store identity, release versioning, or migration rollback history as part of Task 14.