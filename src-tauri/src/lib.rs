mod actions;
mod config;
mod core_logic;
mod history;
mod keyboard_monitor;
mod llm;
mod popover;
mod replacement;
mod screenshot;
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
async fn process_text_stream(
    action: String,
    text: String,
    copy: Option<bool>,
    on_event: Channel<String>,
) -> Result<actions::ProcessResult, String> {
    let action = actions::TextAction::parse(&action)?;
    let copy = copy.unwrap_or(true);

    tauri::async_runtime::spawn_blocking(move || {
        actions::process_stream(action, &text, copy, |piece| {
            let _ = on_event.send(piece.to_owned());
        })
    })
    .await
    .map_err(|error| format!("process task failed: {error}"))?
}

#[tauri::command]
fn resize_popover(app: tauri::AppHandle, height: f64) -> Result<(), String> {
    let Some(window) = app.get_webview_window("popover") else {
        return Ok(());
    };

    let height = if height.is_finite() {
        height.clamp(180.0, 1200.0)
    } else {
        420.0
    };

    window
        .set_size(tauri::LogicalSize::new(380.0, height))
        .map_err(|error| format!("resize popover: {error}"))?;

    let position = window
        .outer_position()
        .map_err(|error| format!("read popover position: {error}"))?;
    let size = window
        .outer_size()
        .map_err(|error| format!("read popover size: {error}"))?;

    let monitors = window
        .available_monitors()
        .map_err(|error| format!("list monitors: {error}"))?;

    if let Some(monitor) = monitors.into_iter().find(|monitor| {
        let origin = monitor.position();
        let bounds = monitor.size();
        position.x >= origin.x
            && position.y >= origin.y
            && position.x < origin.x + bounds.width as i32
            && position.y < origin.y + bounds.height as i32
    }) {
        let top = monitor.position().y;
        let bottom = top + monitor.size().height as i32;
        let overflow = position.y + size.height as i32 - bottom;

        if overflow > 0 {
            window
                .set_position(tauri::PhysicalPosition::new(
                    position.x,
                    (position.y - overflow - 12).max(top),
                ))
                .map_err(|error| format!("reposition popover: {error}"))?;
        }
    }

    Ok(())
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

/// Download on a worker thread so the UI stays responsive, then auto-load the model.
#[tauri::command]
async fn download_model(repo: String, file: String) -> Result<llm::ModelStatus, String> {
    let repo_for_download = repo.clone();
    let file_for_download = file.clone();
    tauri::async_runtime::spawn_blocking(move || {
        llm::download_model(&repo_for_download, &file_for_download).map_err(|error| {
            format!("Model download failed. Check your internet connection and try again. {error}")
        })
    })
    .await
    .map_err(|error| format!("download task failed: {error}"))??;

    let mut cfg = config::load();
    cfg.model_repo = repo;
    cfg.model_file = file;
    config::save(&cfg)?;

    // Auto-load so Translate / Rewrite works immediately after download.
    // Loading can take a few seconds; keep it on a worker thread too.
    let cfg_for_load = cfg.clone();
    let load_result = tauri::async_runtime::spawn_blocking(move || llm::ensure_loaded(&cfg_for_load))
        .await
        .map_err(|error| format!("load task failed: {error}"))?;

    if let Err(error) = load_result {
        // Download succeeded; surface load failure but still return status so UI can recover.
        let status = llm::model_status(&cfg);
        return Err(format!(
            "Model downloaded, but auto-load failed: {error}. Status: downloaded={}, loaded={}.",
            status.downloaded, status.loaded
        ));
    }

    Ok(llm::model_status(&cfg))
}

#[tauri::command]
async fn load_model() -> Result<llm::ModelStatus, String> {
    let cfg = config::load();
    let cfg_for_load = cfg.clone();
    tauri::async_runtime::spawn_blocking(move || llm::ensure_loaded(&cfg_for_load))
        .await
        .map_err(|error| format!("load task failed: {error}"))?
        .map_err(|error| error)?;
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
            resize_popover,
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
            if let Some(mode) = screenshot::from_env() {
                screenshot::open_capture_window(app.handle(), mode)?;
                return Ok(());
            }

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
