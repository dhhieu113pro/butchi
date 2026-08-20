use anyhow::{anyhow, Result};
use arboard::Clipboard;
use std::thread;
use std::time::Duration;
use windows_sys::Win32::UI::Input::KeyboardAndMouse::{
    SendInput, INPUT, INPUT_KEYBOARD, KEYEVENTF_KEYUP, KEYBDINPUT, VK_CONTROL,
};

const VK_C: u16 = 0x43;
const VK_V: u16 = 0x56;

fn send_ctrl_combo(vk_key: u16) {
    unsafe {
        let mut inputs = [
            INPUT {
                r#type: INPUT_KEYBOARD,
                Anonymous: windows_sys::Win32::UI::Input::KeyboardAndMouse::INPUT_0 {
                    ki: KEYBDINPUT {
                        wVk: VK_CONTROL as u16,
                        wScan: 0,
                        dwFlags: 0,
                        time: 0,
                        dwExtraInfo: 0,
                    },
                },
            },
            INPUT {
                r#type: INPUT_KEYBOARD,
                Anonymous: windows_sys::Win32::UI::Input::KeyboardAndMouse::INPUT_0 {
                    ki: KEYBDINPUT {
                        wVk: vk_key,
                        wScan: 0,
                        dwFlags: 0,
                        time: 0,
                        dwExtraInfo: 0,
                    },
                },
            },
            INPUT {
                r#type: INPUT_KEYBOARD,
                Anonymous: windows_sys::Win32::UI::Input::KeyboardAndMouse::INPUT_0 {
                    ki: KEYBDINPUT {
                        wVk: vk_key,
                        wScan: 0,
                        dwFlags: KEYEVENTF_KEYUP,
                        time: 0,
                        dwExtraInfo: 0,
                    },
                },
            },
            INPUT {
                r#type: INPUT_KEYBOARD,
                Anonymous: windows_sys::Win32::UI::Input::KeyboardAndMouse::INPUT_0 {
                    ki: KEYBDINPUT {
                        wVk: VK_CONTROL as u16,
                        wScan: 0,
                        dwFlags: KEYEVENTF_KEYUP,
                        time: 0,
                        dwExtraInfo: 0,
                    },
                },
            },
        ];

        SendInput(
            inputs.len() as u32,
            inputs.as_mut_ptr(),
            std::mem::size_of::<INPUT>() as i32,
        );
    }
}

pub fn capture_selected_text() -> Result<(String, Option<String>)> {
    let mut clipboard = Clipboard::new().map_err(|e| anyhow!("Failed clipboard init: {}", e))?;
    let backup = clipboard.get_text().ok();

    // Clear current clipboard or write sentinel to ensure we detect new Ctrl+C copy
    let _ = clipboard.set_text("");

    // Simulate Ctrl+C
    send_ctrl_combo(VK_C);
    thread::sleep(Duration::from_millis(100));

    let selected = clipboard.get_text().unwrap_or_default();
    let trimmed = selected.trim().to_string();

    Ok((trimmed, backup))
}

pub fn inject_text(new_text: &str, backup: Option<String>) -> Result<()> {
    let mut clipboard = Clipboard::new().map_err(|e| anyhow!("Failed clipboard init: {}", e))?;
    clipboard
        .set_text(new_text)
        .map_err(|e| anyhow!("Failed setting clipboard text: {}", e))?;

    // Simulate Ctrl+V to paste new text over selected text
    send_ctrl_combo(VK_V);
    thread::sleep(Duration::from_millis(150));

    // Restore backup clipboard text if available
    if let Some(old_text) = backup {
        let _ = clipboard.set_text(old_text);
    }

    Ok(())
}
