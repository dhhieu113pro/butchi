mod actions;
mod config;
mod core_logic;
mod history;
mod keyboard_monitor;
mod llm;
mod popover;
mod replacement;
mod selection;
mod selection_monitor;
mod tray;

use tauri::{ipc::Channel, Manager, WebviewUrl, WebviewWindowBuilder};

#[tauri::command]
fn process_text(action: String, text: String, copy: Option<bool>) -> Result<actions::ProcessResult, String> {
    let action = actions::TextAction::parse(&action)?;
    actions::process(action, &text, copy.unwrap_or(true))
}

#[tauri::command]
fn process_text_stream(action: String, text: String, copy: Option<bool>, on_event: Channel<String>) -> Result<actions::ProcessResult, String> {
    let action = actions::TextAction::parse(&action)?;
    actions::process_stream(action, &text, copy.unwrap_or(true), |piece| {
        let _ = on_event.send(piece.to_owned());
    })
}

#[tauri::command]
fn remember_selection_target() -> Result<(), String> { replacement::remember_selection_target() }

#[tauri::command]
fn replace_selected_text(text: String) -> Result<(), String> { replacement::replace_selected_text(&text) }

#[tauri::command]
fn get_config() -> config::AppConfig { config::load() }

#[tauri::command]
fn save_config(config: config::AppConfig) -> Result<config::AppConfig, String> {
    config::save(&config)?;
    history::apply_retention()?;
    llm::unload();
    Ok(config::load())
}

#[tauri::command]
fn set_target_language(language: String) -> Result<config::AppConfig, String> {
    let mut cfg = config::load();
    config::update_target_language(&mut cfg, &language)?;
    config::save(&cfg)?;
    Ok(cfg)
}

#[tauri::command]
fn list_models() -> Vec<config::ModelOption> { config::model_catalog() }

#[tauri::command]
fn get_model_status() -> llm::ModelStatus {
    let cfg = config::load();
    llm::model_status(&cfg)
}

#[tauri::command]
fn download_model(repo: String, file: String) -> Result<llm::ModelStatus, String> {
    llm::download_model(&repo, &file).map_err(|error| {
        format!("Model download failed. Check your internet connection and try again. {error}")
    })?;
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
fn list_history(limit: Option<usize>) -> Result<Vec<history::HistoryEntry>, String> { history::list(limit) }

#[tauri::command]
fn search_history(query: Option<String>, action: Option<String>, limit: Option<usize>) -> Result<Vec<history::HistoryEntry>, String> {
    history::search(query.as_deref(), action.as_deref(), limit)
}

#[tauri::command]
fn delete_history_entry(id: String) -> Result<(), String> { history::delete(&id) }

#[tauri::command]
fn clear_history() -> Result<(), String> { history::clear() }

#[tauri::command]
fn clear_local_ai_data() -> Result<(), String> {
    llm::unload();
    history::clear()?;
    let models = config::models_dir()?;
    if models.exists() {
        std::fs::remove_dir_all(&models).map_err(|e| format!("remove downloaded models: {e}"))?;
    }
    std::fs::create_dir_all(&models).map_err(|e| format!("recreate models directory: {e}"))?;
    Ok(())
}

#[tauri::command]
fn open_settings(app: tauri::AppHandle) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("settings") {
        let _ = window.show();
        let _ = window.set_focus();
        return Ok(());
    }
    WebviewWindowBuilder::new(&app, "settings", WebviewUrl::App("settings.html".into()))
        .title("Butchi — Settings")
        .inner_size(520.0, 760.0)
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
            if let Some(window) = app.get_webview_window("settings") { let _ = window.set_focus(); }
        }))
        .invoke_handler(tauri::generate_handler![
            process_text,
            process_text_stream,
            remember_selection_target,
            replace_selected_text,
            get_config,
            save_config,
            set_target_language,
            list_models,
            get_model_status,
            download_model,
            load_model,
            list_history,
            search_history,
            delete_history_entry,
            clear_history,
            clear_local_ai_data,
            open_settings,
        ])
        .setup(|app| {
            tray::create(app)?;
            let _ = history::apply_retention();
            let _ = selection_monitor::start(app.handle().clone());
            let _ = keyboard_monitor::start(app.handle().clone());

            let cfg = config::load();
            if !config::model_is_downloaded(&cfg.model_repo, &cfg.model_file) {
                let _ = tray::open_settings_window(app.handle());
            }
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
