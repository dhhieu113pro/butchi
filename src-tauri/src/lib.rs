mod actions;
mod config;
mod history;
mod keyboard_monitor;
mod llm;
mod popover;
mod selection;
mod selection_monitor;
mod tray;

use tauri::{Manager, WebviewUrl, WebviewWindowBuilder};

#[tauri::command]
fn process_text(
    action: String,
    text: String,
    copy: Option<bool>,
) -> Result<actions::ProcessResult, String> {
    let action = actions::TextAction::parse(&action)?;
    actions::process(action, &text, copy.unwrap_or(true))
}

#[tauri::command]
fn get_config() -> config::AppConfig {
    config::load()
}

#[tauri::command]
fn save_config(config: config::AppConfig) -> Result<config::AppConfig, String> {
    config::save(&config)?;
    llm::unload();
    Ok(config::load())
}

#[tauri::command]
fn list_models() -> Vec<config::ModelOption> {
    config::model_catalog()
}

#[tauri::command]
fn get_model_status() -> llm::ModelStatus {
    let cfg = config::load();
    llm::model_status(&cfg)
}

#[tauri::command]
fn download_model(repo: String, file: String) -> Result<llm::ModelStatus, String> {
    llm::download_model(&repo, &file)?;
    let mut cfg = config::load();
    cfg.model_repo = repo;
    cfg.model_file = file;
    config::save(&cfg)?;
    llm::unload();
    Ok(llm::model_status(&cfg))
}

#[tauri::command]
fn load_model() -> Result<llm::ModelStatus, String> {
    let cfg = config::load();
    llm::ensure_loaded(&cfg)?;
    Ok(llm::model_status(&cfg))
}

#[tauri::command]
fn list_history(limit: Option<usize>) -> Vec<history::HistoryEntry> {
    history::list(limit)
}

#[tauri::command]
fn clear_history() -> Result<(), String> {
    history::clear()
}

#[tauri::command]
fn open_settings(app: tauri::AppHandle) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("settings") {
        let _ = window.show();
        let _ = window.set_focus();
        return Ok(());
    }

    WebviewWindowBuilder::new(&app, "settings", WebviewUrl::App("settings.html".into()))
        .title("Rust Rewrite — Settings")
        .inner_size(480.0, 720.0)
        .resizable(true)
        .center()
        .build()
        .map_err(|e| format!("open settings: {e}"))?;
    Ok(())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, _, _| {
            if let Some(window) = app.get_webview_window("settings") {
                let _ = window.set_focus();
            }
        }))
        .invoke_handler(tauri::generate_handler![
            process_text,
            get_config,
            save_config,
            list_models,
            get_model_status,
            download_model,
            load_model,
            list_history,
            clear_history,
            open_settings,
        ])
        .setup(|app| {
            tray::create(app)?;
            selection_monitor::start(app.handle().clone());
            keyboard_monitor::start(app.handle().clone());
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
