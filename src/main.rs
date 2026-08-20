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

    let model_path = Path::new(&cfg.model.active_model);
    let tokenizer_path = Path::new("models/tokenizer.json");

    println!("Ensuring GGUF model and tokenizer files are downloaded...");
    downloader::ensure_model_and_tokenizer(
        model_path,
        tokenizer_path,
        &cfg.model.hf_repo,
        &cfg.model.hf_filename,
        |downloaded, total| {
            if let Some(t) = total {
                let pct = (downloaded as f64 / t as f64) * 100.0;
                print!("\rDownloading GGUF model: {:.1}% ({}/{} MB)", pct, downloaded / 1_048_576, t / 1_048_576);
            } else {
                print!("\rDownloaded: {} MB", downloaded / 1_048_576);
            }
        },
    ).await?;
    println!("\nModel and tokenizer files ready!");

    println!("Loading GGUF model into Candle engine...");
    let llama_engine = Arc::new(LlamaEngine::load_from_file(model_path, tokenizer_path)?);
    println!("LLM Engine ready!");

    // Setup channels for keyboard double-ctrl events
    let (tx, rx) = channel::<()>();

    // Start native Windows low-level keyboard hook
    hotkey::start_keyboard_hook(tx, cfg.hotkey.double_ctrl_timeout_ms);

    // Setup System Tray Menu
    let tray_menu = Menu::new();
    let _item_info = MenuItem::new("🤖 English Grammar Rewriter (Double-Ctrl)", false, None);
    let item_config = MenuItem::new("⚙️ Open Config (config.toml)", true, None);
    let item_exit = MenuItem::new("❌ Exit", true, None);

    let _ = tray_menu.append(&_item_info);
    let _ = tray_menu.append(&item_config);
    let _ = tray_menu.append(&item_exit);

    // Load simple default tray icon (create 32x32 RGBA icon buffer)
    let icon_rgba = vec![100u8; 32 * 32 * 4];
    let icon = tray_icon::Icon::from_rgba(icon_rgba, 32, 32).expect("Failed to create tray icon");

    let _tray_icon = TrayIconBuilder::new()
        .with_menu(Box::new(tray_menu))
        .with_tooltip("Grammar Rewriter (Double-Ctrl)")
        .with_icon(icon)
        .build()?;

    println!("System Tray active! Press double-Ctrl in any app to rewrite highlighted text.");

    let is_busy = Arc::new(Mutex::new(false));
    let engine_clone = llama_engine.clone();
    let cfg_clone = cfg.clone();

    // Background worker for processing rewrite requests
    tokio::spawn(async move {
        while let Ok(()) = rx.recv() {
            let busy_guard = is_busy.clone();
            {
                let mut busy = busy_guard.lock().unwrap();
                if *busy {
                    continue; // Skip if already processing a request
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

                        // Run LLM rewrite
                        let eng = engine_clone.clone();
                        let sys_prompt = cfg_clone.prompt.system_prompt.clone();
                        let text_to_fix = selected_text.clone();

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
