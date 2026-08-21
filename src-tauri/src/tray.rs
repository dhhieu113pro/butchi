use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem},
    tray::TrayIconBuilder,
    App,
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
    let separator = PredefinedMenuItem::separator(app)?;
    let quit = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;
    let menu = Menu::with_items(app, &[&status, &shortcut, &separator, &quit])?;

    let mut tray = TrayIconBuilder::with_id("main")
        .menu(&menu)
        .tooltip("Rust Rewrite — Ctrl+Alt+G")
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id().as_ref() {
            "quit" => app.exit(0),
            _ => {}
        });

    if let Some(icon) = app.default_window_icon() {
        tray = tray.icon(icon.clone());
    }

    tray.build(app)?;
    Ok(())
}
