use tauri::{LogicalSize, Manager, WebviewUrl, WebviewWindowBuilder};

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

    pub fn is_settings(self) -> bool {
        matches!(self, Self::SettingsLight | Self::SettingsDark)
    }
}

pub fn from_env() -> Option<ScreenshotMode> {
    std::env::var("BUTCHI_SCREENSHOT_MODE")
        .ok()
        .as_deref()
        .and_then(ScreenshotMode::parse)
}

pub fn open_capture_window(
    app: &tauri::AppHandle,
    mode: ScreenshotMode,
) -> Result<(), Box<dyn std::error::Error>> {
    if mode.is_settings() {
        let window = WebviewWindowBuilder::new(
            app,
            "settings",
            WebviewUrl::App("settings.html".into()),
        )
        .title("Butchi — Settings")
        .inner_size(920.0, 720.0)
        .resizable(false)
        .center()
        .build()?;
        window.eval(&format!(
            "window.location.replace('/settings.html?screenshot={}');",
            mode.as_str()
        ))?;
        window.set_focus()?;
        return Ok(());
    }

    let window = app
        .get_webview_window("popover")
        .ok_or("popover window is unavailable")?;
    window.set_size(LogicalSize::new(420.0, 520.0))?;
    window.eval(&format!(
        "window.location.replace('/?screenshot={}');",
        mode.as_str()
    ))?;
    window.show()?;
    window.set_focus()?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_all_supported_modes() {
        assert_eq!(ScreenshotMode::parse("popover-light"), Some(ScreenshotMode::PopoverLight));
        assert_eq!(ScreenshotMode::parse("popover-dark"), Some(ScreenshotMode::PopoverDark));
        assert_eq!(ScreenshotMode::parse("settings-light"), Some(ScreenshotMode::SettingsLight));
        assert_eq!(ScreenshotMode::parse("settings-dark"), Some(ScreenshotMode::SettingsDark));
    }

    #[test]
    fn rejects_unknown_modes() {
        assert_eq!(ScreenshotMode::parse(""), None);
        assert_eq!(ScreenshotMode::parse("foo"), None);
    }

    #[test]
    fn reports_settings_modes() {
        assert!(ScreenshotMode::SettingsLight.is_settings());
        assert!(ScreenshotMode::SettingsDark.is_settings());
        assert!(!ScreenshotMode::PopoverLight.is_settings());
        assert!(!ScreenshotMode::PopoverDark.is_settings());
    }
}
