<p align="center">
  <img src="docs/assets/logo.svg" width="128" height="128" alt="Butchi logo"/>
</p>

<h1 align="center">Butchi</h1>

<p align="center">
  <em>bút chì</em> — private local Translate & Rewrite for Windows
</p>

<p align="center">
  <img src="docs/assets/butchi-hero.svg" width="960" alt="Butchi — Translate and Rewrite anywhere with a private local LLM"/>
</p>

<p align="center">
  <a href="https://github.com/dhhieu113pro/butchi/actions/workflows/ci.yml"><img src="https://github.com/dhhieu113pro/butchi/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
</p>

## What Butchi does

Butchi is a Windows tray utility that captures selected text and opens a small cursor-adjacent popover for **Translate** and **Rewrite**. Generation runs through a local GGUF model via llama.cpp.

### Highlights

- Automatic popover for supported mouse selections.
- **Double-Ctrl** for keyboard-selected text.
- Windows UI Automation capture with guarded clipboard fallback.
- Local GGUF inference via `llama-cpp-2`.
- Streaming Translate / Rewrite output.
- Optional **Copy result**, **Replace selected text**, or no automatic action.
- Editable Translate and Rewrite system prompts.
- Prompt profiles: Natural, Literal, Professional, Grammar only, Shorter, More polite, Simple language, and Custom.
- Favorite target languages with one-click rerun.
- Searchable local history with configurable retention.
- Auto / CPU / GPU inference preference with CPU fallback when supported by the build.
- System / Light / Dark appearance.
- First-run model setup and local-data deletion controls.

## Privacy

Selected text, prompts, generated results, and history are processed/stored locally and are not sent to a cloud AI service. Network access is used when the user explicitly downloads a GGUF model from Hugging Face.

See [PRIVACY.md](PRIVACY.md).

## Development

Requirements: Rust, Node.js, CMake, a C/C++ toolchain, and LLVM/libclang.

```sh
npm install
npm run dev
```

The local LLM feature is enabled by default.

### GPU builds

```sh
# NVIDIA CUDA
npm run tauri build -- --features cuda

# Vulkan
npm run tauri build -- --features vulkan
```

The default public build can still be CPU-only; Auto mode only uses GPU backends that were compiled into that binary.

## CI

GitHub Actions runs on every push/PR to `main` for:

- Windows x64 (`windows-latest`)
- Windows ARM64 (`windows-11-arm`)

CI runs frontend type/build checks and Rust `cargo check`, `cargo test --all-targets`, and Clippy with the local LLM enabled.

## Model setup

On first launch, Settings opens automatically if no GGUF model is installed.

1. Choose a model.
2. Click **Download** (default Qwen3.5 0.8B Q4_K_M is about 530 MB).
3. Click **Load model**.
4. Select text and use Butchi.

Models are stored in Butchi's OS application-data directory.

## Windows releases

The GitHub release workflow builds x64 and ARM64 NSIS installers from `v*` tags.

For a normal direct-download build, WebView2 may use a smaller bootstrapper configuration. The dedicated Microsoft Store configuration is different: it embeds the offline WebView2 installer so the submitted EXE is a standalone installer.

## Microsoft Store

Butchi uses the Partner Center **MSI/EXE app** submission path.

The Store workflow (`.github/workflows/publish-windows-store.yml`) enforces the important submission constraints:

- app/tag version consistency;
- x64 + ARM64 builds;
- standalone/offline WebView2 installation;
- required CA-trusted Authenticode certificate;
- Partner Center publisher injected from a GitHub secret;
- Authenticode verification;
- silent `/S` install and uninstall smoke test;
- immutable versioned GitHub Release assets.

Required GitHub secrets:

- `STORE_PUBLISHER`
- `WINDOWS_CERTIFICATE`
- `WINDOWS_CERTIFICATE_PASSWORD`

The certificate must be a CA-trusted Authenticode code-signing certificate. Microsoft Store does **not** re-sign EXE/MSI submissions.

Full listing copy, certification notes, screenshot plan, system requirements, and release checklist are in [docs/STORE_SUBMISSION.md](docs/STORE_SUBMISSION.md).

## Screenshots

<p align="center">
  <img src="docs/assets/screenshot-popover.svg" width="640" alt="Butchi popover"/>
</p>

<p align="center">
  <img src="docs/assets/screenshot-settings.svg" width="640" alt="Butchi settings"/>
</p>

These SVGs are repository illustrations. For Microsoft Store submission, capture real PNG/JPEG screenshots from the signed release candidate.

## License

[MIT](LICENSE)
