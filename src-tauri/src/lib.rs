mod actions;
mod keyboard_monitor;
mod popover;
mod selection;
mod selection_monitor;
mod tray;

use tauri_plugin_global_shortcut::ShortcutState;

#[tauri::command]
fn process_text(action: String, text: String) -> Result<actions::ProcessResult, String> {
    let action = actions::TextAction::parse(&action)?;
    actions::process(action, &text)
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|_, _, _| {}))
        .invoke_handler(tauri::generate_handler![process_text])
        .setup(|app| {
            tray::create(app)?;
            selection_monitor::start(app.handle().clone())?;
            keyboard_monitor::start(app.handle().clone())?;

            #[cfg(desktop)]
            app.handle().plugin(
                tauri_plugin_global_shortcut::Builder::new()
                    .with_shortcuts(["ctrl+alt+g"])?
                    .with_handler(|app, _, event| {
                        if event.state == ShortcutState::Released {
                            popover::capture_and_show(app.clone());
                        }
                    })
                    .build(),
            )?;

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
