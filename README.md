# Butchi

<p align="center">
  <img src="src/Butchi.App/Assets/ButchiLogo.png" alt="Butchi logo" width="128" height="128" />
</p>

Windows-first local Translate & Rewrite utility built with .NET 10, Avalonia, and LLamaSharp.

Butchi runs local GGUF inference on Windows and provides global Double-Ctrl activation, Windows UI Automation with guarded clipboard fallback, local SQLite history, and x64/ARM64 packaging.

## Architecture

- .NET 10 / C# 14
- Avalonia native desktop UI
- LLamaSharp / llama.cpp local GGUF inference
- Windows UI Automation with guarded clipboard fallback
- Global Double-Ctrl activation
- SQLite local history
- Windows x64 and ARM64 packaging

## Legacy implementation

The former Tauri/Rust implementation is preserved in the canonical repository through the permanent `legacy-tauri` branch and the pre-Avalonia cutover tag documented in `docs/production-cutover.md`.

Development follows the approved Superpowers design and TDD implementation plans under `docs/superpowers/`.
