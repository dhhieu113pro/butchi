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

fn capture_url(mode: ScreenshotMode) -> WebviewUrl {
    let path = if mode.is_settings() {
        format!("settings.html?screenshot={}", mode.as_str())
    } else {
        format!("index.html?screenshot={}", mode.as_str())
    };
    WebviewUrl::App(path.into())
}

/// Open a dedicated, visible, decorated window for CI screenshot capture.
/// Query-string mode is embedded in the initial URL so the frontend can seed
/// deterministic demo content without racing `window.eval`.
pub fn open_capture_window(
    app: &tauri::AppHandle,
    mode: ScreenshotMode,
) -> Result<(), Box<dyn std::error::Error>> {
    // Hide the default invisible popover from tauri.conf so it cannot steal
    // focus or the native title used by FindWindow-based capture.
    // Do NOT close+recreate with label "popover": close is async and the label
    // stays reserved briefly, causing "webview with label `popover` already exists".
    if let Some(existing) = app.get_webview_window("popover") {
        let _ = existing.hide();
        let _ = existing.set_skip_taskbar(true);
    }
    if let Some(existing) = app.get_webview_window("settings") {
        let _ = existing.hide();
        let _ = existing.set_skip_taskbar(true);
    }

    // Distinctive titles that include "Butchi" so FindWindow is reliable on
    // noisy CI runners. Use *unique* labels so we never collide with the
    // config-defined windows still alive in the same process.
    let (label, title, width, height) = if mode.is_settings() {
        (
            "screenshot-settings",
            "Butchi — Settings",
            920.0,
            720.0,
        )
    } else {
        (
            "screenshot-popover",
            "Butchi — Text actions",
            420.0,
            520.0,
        )
    };

    // Idempotent reuse (unlikely in CI one-shot processes).
    if let Some(window) = app.get_webview_window(label) {
        let _ = window.set_title(title);
        let _ = window.set_size(LogicalSize::new(width, height));
        let _ = window.show();
        let _ = window.set_focus();
        let _ = window.set_always_on_top(true);
        return Ok(());
    }

    let window = WebviewWindowBuilder::new(app, label, capture_url(mode))
        .title(title)
        .inner_size(width, height)
        .resizable(false)
        // Decorated + visible + taskbar so FindWindow/GetWindowRect are reliable on CI.
        .decorations(true)
        .visible(true)
        .skip_taskbar(false)
        .always_on_top(true)
        .center()
        .build()?;

    let _ = window.set_size(LogicalSize::new(width, height));
    let _ = window.set_title(title);
    let _ = window.show();
    let _ = window.set_focus();
    let _ = window.set_always_on_top(true);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_all_supported_modes() {
        assert_eq!(
            ScreenshotMode::parse("popover-light"),
            Some(ScreenshotMode::PopoverLight)
        );
        assert_eq!(
            ScreenshotMode::parse("popover-dark"),
            Some(ScreenshotMode::PopoverDark)
        );
        assert_eq!(
            ScreenshotMode::parse("settings-light"),
            Some(ScreenshotMode::SettingsLight)
        );
        assert_eq!(
            ScreenshotMode::parse("settings-dark"),
            Some(ScreenshotMode::SettingsDark)
        );
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
