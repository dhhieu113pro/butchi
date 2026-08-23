#[cfg(target_os = "windows")]
mod windows {
    use std::{
        sync::atomic::{AtomicUsize, Ordering},
        thread,
        time::Duration,
    };

    use arboard::Clipboard;
    use windows_sys::Win32::UI::{
        Input::KeyboardAndMouse::{keybd_event, KEYEVENTF_KEYUP, VK_CONTROL},
        WindowsAndMessaging::GetForegroundWindow,
    };

    use crate::core_logic;

    static TARGET_WINDOW: AtomicUsize = AtomicUsize::new(0);

    pub fn remember_selection_target() -> Result<(), String> {
        let foreground = unsafe { GetForegroundWindow() } as usize;
        if foreground == 0 {
            return Err("no foreground window is available for replacement".into());
        }
        TARGET_WINDOW.store(foreground, Ordering::Release);
        Ok(())
    }

    pub fn replace_selected_text(text: &str) -> Result<(), String> {
        if text.is_empty() {
            return Err("cannot replace selection with empty text".into());
        }

        let target = TARGET_WINDOW.load(Ordering::Acquire);
        let foreground = unsafe { GetForegroundWindow() } as usize;
        core_logic::validate_replace_target(target, foreground)?;

        let mut clipboard = Clipboard::new().map_err(|e| format!("clipboard unavailable: {e}"))?;
        clipboard.set_text(text.to_owned()).map_err(|e| format!("failed to prepare replacement text: {e}"))?;

        thread::sleep(Duration::from_millis(35));
        unsafe {
            keybd_event(VK_CONTROL as u8, 0, 0, 0);
            keybd_event(b'V', 0, 0, 0);
            keybd_event(b'V', 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL as u8, 0, KEYEVENTF_KEYUP, 0);
        }
        Ok(())
    }

    #[cfg(test)]
    mod tests {
        use super::*;

        #[test]
        fn empty_replacement_is_rejected_before_windows_calls() {
            assert_eq!(replace_selected_text("").unwrap_err(), "cannot replace selection with empty text");
        }
    }
}

#[cfg(target_os = "windows")]
pub use windows::{remember_selection_target, replace_selected_text};

#[cfg(not(target_os = "windows"))]
pub fn remember_selection_target() -> Result<(), String> {
    Err("Replace selected text is currently supported on Windows only".into())
}

#[cfg(not(target_os = "windows"))]
pub fn replace_selected_text(_text: &str) -> Result<(), String> {
    Err("Replace selected text is currently supported on Windows only".into())
}
