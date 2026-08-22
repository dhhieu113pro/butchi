# Rust Rewrite

[![CI](https://github.com/dhhieu113pro/rust-rewrite/actions/workflows/ci.yml/badge.svg)](https://github.com/dhhieu113pro/rust-rewrite/actions/workflows/ci.yml)

A Windows-first Tauri utility for acting on selected text from a small cursor-adjacent popover.

## Features

- Tray-only background app (no centered main window)
- Automatic popover after mouse-drag or double-click text selection
- Double-Ctrl tap for keyboard selections
- Windows UI Automation capture (no clipboard mutation) with guarded clipboard fallback
- **Translate** and **Rewrite** actions
- **Settings** window (tray → Settings):
  - Enable / disable Translate and Rewrite
  - Target language for translation
  - Editable rewrite system prompt
  - Local LLM model picker + Hugging Face download
  - Backend indicator (`cpu` | `cuda` | `vulkan`) + detected devices
- Local GGUF inference via `llama-cpp-2` (default **Qwen3.5 0.8B** Q4_K_M)
- Optional GPU offload with Cargo features `cuda` or `vulkan`

## Development

Requirements: Rust, Node, CMake, and a C/C++ toolchain (needed by `llama-cpp-2`).

```sh
npm install
npm run tauri dev
```

### GPU builds

```sh
# NVIDIA CUDA (CUDA Toolkit required)
npm run tauri dev -- -- --features cuda

# Vulkan (Vulkan SDK required on Windows)
npm run tauri dev -- -- --features vulkan
```

After the tray icon appears:

1. Open **Settings** from the tray menu
2. Download the default **Qwen3.5 0.8B** model
3. Click **Load model**
4. Select text in a browser/editor — use Translate / Rewrite

## CI

GitHub Actions (`.github/workflows/ci.yml`) runs on every push/PR to `main`:

| Job | Platforms |
|-----|-----------|
| `test` | **windows-latest**, **ubuntu-latest**, **macos-latest** (Windows first) |
| Steps | `tsc --noEmit`, `cargo check --no-default-features`, `cargo test` |
| `feature-gate` | Linux only — best-effort `--features llm` check |

CI skips the `llm` feature (no llama-cpp-sys-2 compile on runners). Local/dev builds include LLM by default.

## Build

```sh
npm run tauri build
# or with GPU:
npm run tauri build -- -- --features cuda
```

Models are stored under the OS app data directory (`…/rust-rewrite/models`).
