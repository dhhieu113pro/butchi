# Rust Rewrite

A Windows-first Tauri utility for acting on selected text from a small cursor-adjacent popover.

## Features

- Tray-only background app (no centered main window)
- Automatic popover after mouse-drag or double-click text selection
- `Ctrl+Alt+G` fallback + double-Ctrl tap for keyboard selections
- Windows UI Automation capture (no clipboard mutation) with guarded clipboard fallback
- **Translate** and **Rewrite** actions
- **Settings** window (tray → Settings):
  - Enable / disable Translate and Rewrite
  - Target language for translation
  - Editable rewrite system prompt
  - Local LLM model picker + Hugging Face download
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

## Build

```sh
npm run tauri build
# or with GPU:
npm run tauri build -- -- --features cuda
```

Models are stored under the OS app data directory (`…/rust-rewrite/models`).
