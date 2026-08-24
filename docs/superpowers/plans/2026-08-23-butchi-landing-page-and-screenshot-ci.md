# Butchi Landing Page + Real Screenshot CI Implementation Plan

**Goal:** Build a first-class `/butchi/` landing page using real CI-generated screenshots from the actual Windows Tauri app.

**Architecture:** `dhhieu113pro/butchi` owns screenshot mode, Windows screenshot CI, canonical PNGs, and static page source. `dhhieu113pro/dhhieu113pro.github.io` remains the Pages orchestrator and assembles `butchi/docs` into `/butchi/` while preserving Quay.

**Spec:** `docs/superpowers/specs/2026-08-23-butchi-landing-page-design.md`

## Tasks

1. Add opt-in `BUTCHI_SCREENSHOT_MODE` runtime support for popover/settings light/dark captures, with background monitors/tray/model execution disabled in capture mode.
2. Add deterministic frontend demo state using the real popover/settings DOM and existing CSS.
3. Add `scripts/capture-ui.ps1` and `.github/workflows/screenshots.yml` to build the real Windows app, capture four target-window PNGs, validate them, upload an artifact, and safely refresh canonical `docs/shots/*` files.
4. Create the Hallmark static landing page under `docs/` with GitHub download primary CTA, Store-coming-soon state, real screenshot figures, privacy section, capability index, responsive theme support, and no fake window chrome.
5. Add lightweight docs checks and update README screenshot/product-site references.
6. Extend `dhhieu113pro.github.io/.github/workflows/pages.yml` to checkout Butchi and copy `butchi/docs/.` to `site/butchi/`; update the root Butchi project card to `./butchi/`; preserve Quay assembly.
7. Run Butchi CI, screenshot CI, Pages CI, inspect generated PNGs, and perform final Hallmark responsive/accessibility review before merge.

## Verification

- Frontend typecheck/build/tests green.
- Rust check/test/strict Clippy green.
- Four screenshot PNGs exist and have non-trivial dimensions/file sizes.
- `/butchi/` and `/quay/` both exist in assembled Pages artifact.
- No horizontal overflow at 320/375/414/768 px.
- Screenshot mode does not affect normal startup.
