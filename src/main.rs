mod clipboard;
mod config;
mod downloader;
mod engine;
mod hotkey;

use anyhow::Result;
use config::Config;
use engine::LlamaEngine;
use std::path::Path;
use std::sync::mpsc::channel;
use std::sync::{Arc, Mutex};
use tray_item::{IconSource, TrayItem};

fn main() -> Result<()> {
    println!("=== Starting Rust Grammar Rewriter (Embedded Candle LLM) ===");

    let config_path = Path::new("config.toml");
    let cfg = Config::load_or_create(config_path)?;
    println!("Config loaded. Double-Ctrl timeout: {}ms", cfg.hotkey.double_ctrl_timeout_ms);

    // Initialize Native Windows System Tray Item
    let mut tray = TrayItem::new("Grammar Rewriter", IconSource::Resource("").into())
        .or_else(|_| TrayItem::new("Grammar Rewriter", IconSource::Resource("")));

    let mut tray = match tray {
        Ok(t) => t,
        Err(_) => {
            // Fallback to empty resource string if icon not specified
            TrayItem::new("Grammar Rewriter", IconSource::Resource("")).expect("Failed to initialize Windows Tray")
        }
    };

    tray.add_menu_item("🤖 English Grammar Rewriter (Double-Ctrl)", || {})?;
    tray.add_menu_item("⚙️ Open Config (config.toml)", || {
        let _ = std::process::Command::new("notepad.exe")
            .arg("config.toml")
            .spawn();
    })?;
    tray.add_menu_item("❌ Exit", || {
        println!("Exiting application...");
        std::process::exit(0);
    })?;

    println!("System Tray Icon active in Windows taskbar!");

    // Start Tokio async runtime for background tasks
    let rt = tokio::runtime::Runtime::new()?;
    rt.block_on(async move {
        let model_engine: Arc<Mutex<Option<Arc<LlamaEngine>>>> = Arc::new(Mutex::new(None));
        let engine_store = model_engine.clone();
        let cfg_clone = cfg.clone();

        // Async task to download & load model in background
        tokio::spawn(async move {
            let model_path = Path::new(&cfg_clone.model.active_model);
            let tokenizer_path = Path::new("models/tokenizer.json");

            println!("Ensuring GGUF model and tokenizer files are downloaded...");
            let dl_res = downloader::ensure_model_and_tokenizer(
                model_path,
                tokenizer_path,
                &cfg_clone.model.hf_repo,
                &cfg_clone.model.hf_filename,
                |downloaded, total| {
                    if let Some(t) = total {
                        let pct = (downloaded as f64 / t as f64) * 100.0;
                        print!("\rDownloading GGUF model: {:.1}% ({}/{} MB)", pct, downloaded / 1_048_576, t / 1_048_576);
                    } else {
                        print!("\rDownloaded: {} MB", downloaded / 1_048_576);
                    }
                },
            ).await;

            if let Err(e) = dl_res {
                eprintln!("\nFailed downloading model: {}", e);
                return;
            }

            println!("\nModel and tokenizer files ready!");
            println!("Loading GGUF model into Candle engine...");

            match LlamaEngine::load_from_file(model_path, tokenizer_path) {
                Ok(engine) => {
                    println!("🎉 LLM Engine fully loaded and ready!");
                    let mut guard = engine_store.lock().unwrap();
                    *guard = Some(Arc::new(engine));
                }
                Err(e) => eprintln!("Failed loading model engine: {}", e),
            }
        });

        // Setup channels for keyboard double-ctrl events
        let (tx, rx) = channel::<()>();

        // Start native Windows low-level keyboard hook
        hotkey::start_keyboard_hook(tx, cfg.hotkey.double_ctrl_timeout_ms);

        let is_busy = Arc::new(Mutex::new(false));
        let engine_access = model_engine.clone();
        let cfg_access = cfg.clone();

        // Worker for processing rewrite requests
        tokio::spawn(async move {
            while let Ok(()) = rx.recv() {
                let engine_opt = {
                    let guard = engine_access.lock().unwrap();
                    guard.clone()
                };

                let llama_engine = match engine_opt {
                    Some(eng) => eng,
                    None => {
                        println!("\n⚠️ Model is still downloading/loading. Please wait...");
                        continue;
                    }
                };

                let busy_guard = is_busy.clone();
                {
                    let mut busy = busy_guard.lock().unwrap();
                    if *busy {
                        continue; // Skip if processing a request
                    }
                    *busy = true;
                }

                println!("\n⚡ Double-Ctrl triggered!");

                // Capture text
                match clipboard::capture_selected_text() {
                    Ok((selected_text, backup)) => {
                        if selected_text.is_empty() {
                            println!("No text selected or clipboard empty.");
                        } else {
                            println!("Original text: {:?}", selected_text);

                            let sys_prompt = cfg_access.prompt.system_prompt.clone();
                            let text_to_fix = selected_text.clone();
                            let eng = llama_engine.clone();

                            let res = tokio::task::spawn_blocking(move || {
                                eng.rewrite_text(&text_to_fix, &sys_prompt)
                            }).await;

                            match res {
                                Ok(Ok(fixed_text)) => {
                                    println!("✨ Rewritten text: {:?}", fixed_text);
                                    if let Err(e) = clipboard::inject_text(&fixed_text, backup) {
                                        eprintln!("Failed injecting text: {}", e);
                                    }
                                }
                                Ok(Err(e)) => eprintln!("LLM rewrite error: {}", e),
                                Err(e) => eprintln!("Task join error: {}", e),
                            }
                        }
                    }
                    Err(e) => eprintln!("Failed capturing text: {}", e),
                }

                let mut busy = busy_guard.lock().unwrap();
                *busy = false;
            }
        });

        // Keep main thread event pump running for tray & hotkeys
        loop {
            tokio::time::sleep(tokio::time::Duration::from_secs(3600)).await;
        }
    })
}
