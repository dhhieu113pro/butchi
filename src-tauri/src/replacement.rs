#[cfg(target_os = "windows")]
pub fn replace_selected_text(text: &str) -> Result<(), String> {
    use std::{thread, time::Duration};

    use arboard::Clipboard;
    use windows_sys::Win32::UI::{
        Input::KeyboardAndMouse::{keybd_event, KEYEVENTF_KEYUP, VK_CONTROL},
        WindowsAndMessaging::GetForegroundWindow,
    };

    if text.is_empty() {
        return Err("cannot replace selection with empty text".into());
    }

    let foreground = unsafe { GetForegroundWindow() };
    if foreground.is_null() {
        return Err("no foreground window is available for replacement".into());
    }

    let mut clipboard = Clipboard::new().map_err(|e| format!("clipboard unavailable: {e}"))?;
    clipboard
        .set_text(text.to_owned())
        .map_err(|e| format!("failed to prepare replacement text: {e}"))?;

    // The selection popover is non-focusable, so the originating application
    // remains the foreground window. Give the clipboard a moment to settle,
    // then synthesize Ctrl+V to replace the still-active selection.
    thread::sleep(Duration::from_millis(35));
    unsafe {
        keybd_event(VK_CONTROL as u8, 0, 0, 0);
        keybd_event(b'V', 0, 0, 0);
        keybd_event(b'V', 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL as u8, 0, KEYEVENTF_KEYUP, 0);
    }

    Ok(())
}

#[cfg(not(target_os = "windows"))]
pub fn replace_selected_text(_text: &str) -> Result<(), String> {
    Err("Replace selected text is currently supported on Windows only".into())
}
