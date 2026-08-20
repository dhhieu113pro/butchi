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
use tray_icon::menu::{Menu, MenuItem};
use tray_icon::{TrayIconBuilder, TrayIconEvent};
use winit::event_loop::{ControlFlow, EventLoop};

#[tokio::main]
async fn main() -> Result<()> {
    println!("=== Starting Rust Grammar Rewriter (Embedded Candle LLM) ===");

    let config_path = Path::new("config.toml");
    let cfg = Config::load_or_create(config_path)?;
    println!("Config loaded. Double-Ctrl timeout: {}ms", cfg.hotkey.double_ctrl_timeout_ms);

    // Setup System Tray Menu IMMEDIATELY on startup so icon appears right away
    let tray_menu = Menu::new();
    let _item_info = MenuItem::new("🤖 English Grammar Rewriter (Double-Ctrl)", false, None);
    let item_config = MenuItem::new("⚙️ Open Config (config.toml)", true, None);
    let item_exit = MenuItem::new("❌ Exit", true, None);

    let _ = tray_menu.append(&_item_info);
    let _ = tray_menu.append(&item_config);
    let _ = tray_menu.append(&item_exit);

    // Create 32x32 RGBA icon (cyan/blue square for easy visibility)
    let mut icon_rgba = vec![0u8; 32 * 32 * 4];
    for pixel in icon_rgba.chunks_exact_mut(4) {
        pixel[0] = 0;   // R
        pixel[1] = 180; // G
        pixel[2] = 255; // B
        pixel[3] = 255; // A (Fully opaque)
    }
    let icon = tray_icon::Icon::from_rgba(icon_rgba, 32, 32).expect("Failed to create tray icon");

    let _tray_icon = TrayIconBuilder::new()
        .with_menu(Box::new(tray_menu))
        .with_tooltip("Grammar Rewriter (Initializing...)")
        .with_icon(icon)
        .build()?;

    println!("System Tray Icon active! (Bottom right taskbar)");

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

    // Background worker for processing rewrite requests
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

    let event_loop = EventLoop::new()?;
    let _ = event_loop.run(|_event, target| {
        target.set_control_flow(ControlFlow::Wait);

        if let Ok(event) = TrayIconEvent::receiver().try_recv() {
            println!("Tray event: {:?}", event);
        }
    });

    Ok(())
}
