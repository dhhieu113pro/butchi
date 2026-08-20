use anyhow::{anyhow, Result};
use arboard::Clipboard;
use std::thread;
use std::time::Duration;
use windows_sys::Win32::UI::Input::KeyboardAndMouse::{
    SendInput, INPUT, INPUT_KEYBOARD, KEYEVENTF_KEYUP, KEYBDINPUT, VK_C, VK_CONTROL, VK_LCONTROL,
    VK_MENU, VK_RCONTROL, VK_SHIFT, VK_V,
};

fn release_key(vk: u16) {
    unsafe {
        let input = INPUT {
            r#type: INPUT_KEYBOARD,
            Anonymous: windows_sys::Win32::UI::Input::KeyboardAndMouse::INPUT_0 {
                ki: KEYBDINPUT {
                    wVk: vk,
                    wScan: 0,
                    dwFlags: KEYEVENTF_KEYUP,
                    time: 0,
                    dwExtraInfo: 0,
                },
            },
        };
        SendInput(1, &input, std::mem::size_of::<INPUT>() as i32);
    }
}

pub fn release_modifier_keys() {
    release_key(VK_CONTROL);
    release_key(VK_LCONTROL);
    release_key(VK_RCONTROL);
    release_key(VK_SHIFT);
    release_key(VK_MENU);
}

fn send_ctrl_combo(vk_code: u16) {
    unsafe {
        release_modifier_keys();
        thread::sleep(Duration::from_millis(20));

        let inputs = [
            INPUT {
                r#type: INPUT_KEYBOARD,
                Anonymous: windows_sys::Win32::UI::Input::KeyboardAndMouse::INPUT_0 {
                    ki: KEYBDINPUT {
                        wVk: VK_CONTROL,
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
                        wVk: vk_code,
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
                        wVk: vk_code,
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
                        wVk: VK_CONTROL,
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
            inputs.as_ptr(),
            std::mem::size_of::<INPUT>() as i32,
        );
    }
}

pub fn capture_selected_text() -> Result<(String, Option<String>)> {
    let mut clipboard = Clipboard::new().map_err(|e| anyhow!("Failed clipboard init: {}", e))?;
    let backup = clipboard.get_text().ok();

    let _ = clipboard.set_text("");
    send_ctrl_combo(VK_C);
    thread::sleep(Duration::from_millis(150));

    let selected = clipboard.get_text().unwrap_or_default();
    let trimmed = selected.trim().to_string();

    Ok((trimmed, backup))
}

pub fn inject_text(new_text: &str, backup: Option<String>) -> Result<()> {
    let mut clipboard = Clipboard::new().map_err(|e| anyhow!("Failed clipboard init: {}", e))?;
    clipboard.set_text(new_text)?;

    println!("[Clipboard] Pasting corrected text: {:?}", new_text);
    send_ctrl_combo(VK_V);
    thread::sleep(Duration::from_millis(150));

    if let Some(old) = backup {
        let _ = clipboard.set_text(&old);
    }

    Ok(())
}
