use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem},
    tray::TrayIconBuilder,
    App, Manager,
};

pub fn create(app: &mut App) -> tauri::Result<()> {
    let status = MenuItem::with_id(
        app,
        "selection-status",
        "Select text — popup opens automatically",
        false,
        None::<&str>,
    )?;
    let shortcut = MenuItem::with_id(
        app,
        "shortcut-status",
        "Fallback shortcut: Ctrl+Alt+G",
        false,
        None::<&str>,
    )?;
    let settings = MenuItem::with_id(app, "settings", "Settings…", true, None::<&str>)?;
    let separator = PredefinedMenuItem::separator(app)?;
    let quit = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;
    let menu = Menu::with_items(app, &[&status, &shortcut, &settings, &separator, &quit])?;

    let mut tray = TrayIconBuilder::with_id("main")
        .menu(&menu)
        .tooltip("Rust Rewrite — Ctrl+Alt+G")
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id().as_ref() {
            "settings" => {
                let handle = app.clone();
                let _ = handle.run_on_main_thread(move || {
                    let _ = crate::open_settings_cmd(&handle);
                });
            }
            "quit" => app.exit(0),
            _ => {}
        });

    if let Some(icon) = app.default_window_icon() {
        tray = tray.icon(icon.clone());
    }

    tray.build(app)?;
    Ok(())
}

// Helper so tray can open settings without circular visibility issues.
pub(crate) fn open_settings_cmd(app: &tauri::AppHandle) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("settings") {
        let _ = window.show();
        let _ = window.set_focus();
        return Ok(());
    }

    tauri::WebviewWindowBuilder::new(
        app,
        "settings",
        tauri::WebviewUrl::App("settings.html".into()),
    )
    .title("Rust Rewrite — Settings")
    .inner_size(440.0, 640.0)
    .resizable(true)
    .center()
    .build()
    .map_err(|e| format!("open settings: {e}"))?;
    Ok(())
}
