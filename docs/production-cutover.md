# Butchi Production Cutover

Before canonical `main` changes, preserve its exact pre-cutover Tauri/Rust commit as:

- branch: `legacy-tauri`
- tag: `pre-avalonia-cutover-2026-08-26`

These refs are permanent and must not be deleted. They are the source-level rollback/reference anchors for the former implementation.

The .NET/Avalonia tree becomes canonical only after the anchors exist. If canonical validation fails, fix forward on the .NET branch or restore `main` from the preserved legacy anchor; never rewrite history to erase either implementation.

The migration repository may be archived only after canonical CI, parity/performance, final migration validation, and release packaging evidence are green.

## Startup readiness

- Ready settings and a successfully loaded configured model produce tray-only startup.
- Missing or invalid settings, or a missing or failed model, produce one mandatory Welcome Setup window.
- Closing Welcome Setup before completion exits Butchi.
- Successful setup transitions directly to tray operation without restarting the process.
