use tauri::{Manager, WebviewUrl, WebviewWindowBuilder};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ScreenshotMode {
    PopoverLight,
    PopoverDark,
    SettingsLight,
    SettingsDark,
}

impl ScreenshotMode {
    pub fn parse(value: &str) -> Option<Self> {
        match value {
            "popover-light" => Some(Self::PopoverLight),
            "popover-dark" => Some(Self::PopoverDark),
            "settings-light" => Some(Self::SettingsLight),
            "settings-dark" => Some(Self::SettingsDark),
            _ => None,
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Self::PopoverLight => "popover-light",
            Self::PopoverDark => "popover-dark",
            Self::SettingsLight => "settings-light",
            Self::SettingsDark => "settings-dark",
        }
    }

    fn is_settings(self) -> bool {
        matches!(self, Self::SettingsLight | Self::SettingsDark)
    }
}

pub fn parse_screenshot_mode() -> Option<ScreenshotMode> {
    std::env::var("BUTCHI_SCREENSHOT_MODE")
        .ok()
        .as_deref()
        .and_then(ScreenshotMode::parse)
}

pub fn open_capture_window(app: &tauri::AppHandle, mode: ScreenshotMode) -> Result<(), String> {
    if mode.is_settings() {
        WebviewWindowBuilder::new(app, "settings", WebviewUrl::App("settings.html".into()))
            .title("Butchi — Settings")
            .inner_size(920.0, 760.0)
            .resizable(false)
            .center()
            .build()
            .map_err(|e| format!("open screenshot settings window: {e}"))?;
    } else if let Some(window) = app.get_webview_window("popover") {
        let _ = window.set_size(tauri::LogicalSize::new(380.0, 420.0));
        let _ = window.center();
        let _ = window.show();
        let _ = window.set_focus();
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_supported_modes() {
        assert_eq!(ScreenshotMode::parse("popover-light"), Some(ScreenshotMode::PopoverLight));
        assert_eq!(ScreenshotMode::parse("popover-dark"), Some(ScreenshotMode::PopoverDark));
        assert_eq!(ScreenshotMode::parse("settings-light"), Some(ScreenshotMode::SettingsLight));
        assert_eq!(ScreenshotMode::parse("settings-dark"), Some(ScreenshotMode::SettingsDark));
    }

    #[test]
    fn rejects_unknown_modes() {
        assert_eq!(ScreenshotMode::parse(""), None);
        assert_eq!(ScreenshotMode::parse("other"), None);
    }
}
