use std::{thread, time::Duration};

use arboard::Clipboard;

enum ClipboardBackup {
    Text(String),
    Image {
        width: usize,
        height: usize,
        bytes: Vec<u8>,
    },
    Empty,
}

impl ClipboardBackup {
    fn read(clipboard: &mut Clipboard) -> Self {
        if let Ok(text) = clipboard.get_text() {
            Self::Text(text)
        } else if let Ok(image) = clipboard.get_image() {
            Self::Image {
                width: image.width,
                height: image.height,
                bytes: image.bytes.into_owned(),
            }
        } else {
            Self::Empty
        }
    }

    fn restore(self, clipboard: &mut Clipboard) -> Result<(), SelectionError> {
        match self {
            Self::Text(text) => clipboard.set_text(text),
            Self::Image {
                width,
                height,
                bytes,
            } => clipboard.set_image(arboard::ImageData {
                width,
                height,
                bytes: bytes.into(),
            }),
            Self::Empty => clipboard.clear(),
        }
        .map_err(|error| SelectionError::Clipboard(format!("failed to restore: {error}")))
    }
}

#[derive(Debug)]
pub enum SelectionError {
    Automation(String),
    Clipboard(String),
    Empty,
    FocusChanged,
    Input,
    ModifiersHeld,
}

impl std::fmt::Display for SelectionError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Automation(message) => write!(formatter, "UI Automation error: {message}"),
            Self::Clipboard(message) => write!(formatter, "clipboard error: {message}"),
            Self::Empty => formatter.write_str("no selected text was captured"),
            Self::FocusChanged => formatter.write_str("the active window changed before capture"),
            Self::Input => formatter.write_str("failed to send the copy shortcut"),
            Self::ModifiersHeld => {
                formatter.write_str("Ctrl, Alt, Shift, or Windows key is still pressed")
            }
        }
    }
}

impl std::error::Error for SelectionError {}

#[cfg(target_os = "windows")]
fn ensure_foreground(expected: Option<usize>) -> Result<(), SelectionError> {
    use windows_sys::Win32::UI::WindowsAndMessaging::GetForegroundWindow;

    if expected.is_some_and(|window| (unsafe { GetForegroundWindow() }) as usize != window) {
        Err(SelectionError::FocusChanged)
    } else {
        Ok(())
    }
}

#[cfg(not(target_os = "windows"))]
fn ensure_foreground(_expected: Option<usize>) -> Result<(), SelectionError> {
    Ok(())
}

#[cfg(target_os = "windows")]
fn clipboard_sequence() -> u32 {
    use windows_sys::Win32::System::DataExchange::GetClipboardSequenceNumber;

    unsafe { GetClipboardSequenceNumber() }
}

#[cfg(not(target_os = "windows"))]
fn clipboard_sequence() -> u32 {
    0
}

#[cfg(target_os = "windows")]
fn ensure_clipboard_is_restorable() -> Result<u32, SelectionError> {
    use windows_sys::Win32::{
        Foundation::{GetLastError, SetLastError},
        System::DataExchange::{CloseClipboard, EnumClipboardFormats, OpenClipboard},
    };

    struct OpenClipboardGuard;
    impl Drop for OpenClipboardGuard {
        fn drop(&mut self) {
            unsafe {
                CloseClipboard();
            }
        }
    }

    let mut opened = false;
    for _ in 0..5 {
        if unsafe { OpenClipboard(std::ptr::null_mut()) } != 0 {
            opened = true;
            break;
        }
        thread::sleep(Duration::from_millis(20));
    }
    if !opened {
        return Err(SelectionError::Clipboard(
            "clipboard is busy; try again".into(),
        ));
    }
    let _guard = OpenClipboardGuard;

    let mut format = 0;
    let mut has_text = false;
    let mut has_image = false;
    loop {
        unsafe { SetLastError(0) };
        format = unsafe { EnumClipboardFormats(format) };
        if format == 0 {
            let error = unsafe { GetLastError() };
            if error != 0 {
                return Err(SelectionError::Clipboard(
                    std::io::Error::from_raw_os_error(error as i32).to_string(),
                ));
            }
            break;
        }

        match format {
            // Plain text plus its locale/synthesized variants.
            1 | 7 | 13 | 16 => has_text = true,
            // Bitmap/DIB formats that arboard can faithfully re-encode.
            2 | 8 | 9 | 17 => has_image = true,
            _ => {
                return Err(SelectionError::Clipboard(
                    "rich clipboard data is present; fallback copy was skipped to protect it"
                        .into(),
                ));
            }
        }
    }

    if has_text && has_image {
        return Err(SelectionError::Clipboard(
            "mixed clipboard data is present; fallback copy was skipped to protect it".into(),
        ));
    }

    Ok(clipboard_sequence())
}

#[cfg(not(target_os = "windows"))]
fn ensure_clipboard_is_restorable() -> Result<u32, SelectionError> {
    Ok(clipboard_sequence())
}

#[cfg(target_os = "windows")]
struct ComApartment;

#[cfg(target_os = "windows")]
impl ComApartment {
    fn initialize() -> Result<Self, SelectionError> {
        use windows::Win32::System::Com::{CoInitializeEx, COINIT_APARTMENTTHREADED};

        unsafe { CoInitializeEx(None, COINIT_APARTMENTTHREADED) }
            .ok()
            .map_err(|error| SelectionError::Automation(error.to_string()))?;
        Ok(Self)
    }
}

#[cfg(target_os = "windows")]
impl Drop for ComApartment {
    fn drop(&mut self) {
        use windows::Win32::System::Com::CoUninitialize;

        unsafe { CoUninitialize() };
    }
}

#[cfg(target_os = "windows")]
pub fn capture_selected_text_automation(
    expected_foreground: Option<usize>,
) -> Result<String, SelectionError> {
    use windows::Win32::{
        System::Com::{CoCreateInstance, CLSCTX_INPROC_SERVER},
        UI::Accessibility::{
            CUIAutomation, IUIAutomation, IUIAutomationTextPattern, UIA_TextPatternId,
        },
    };

    ensure_foreground(expected_foreground)?;
    let _apartment = ComApartment::initialize()?;
    let automation: IUIAutomation =
        unsafe { CoCreateInstance(&CUIAutomation, None, CLSCTX_INPROC_SERVER) }
            .map_err(|error| SelectionError::Automation(error.to_string()))?;
    let mut element = unsafe { automation.GetFocusedElement() }
        .map_err(|error| SelectionError::Automation(error.to_string()))?;
    let walker = unsafe { automation.RawViewWalker() }
        .map_err(|error| SelectionError::Automation(error.to_string()))?;

    // Selection can belong to a document/edit ancestor rather than the focused child.
    for _ in 0..16 {
        if let Ok(pattern) =
            unsafe { element.GetCurrentPatternAs::<IUIAutomationTextPattern>(UIA_TextPatternId) }
        {
            if let Ok(ranges) = unsafe { pattern.GetSelection() } {
                let range_count = unsafe { ranges.Length() }.unwrap_or(0);
                let mut selections = Vec::with_capacity(range_count.max(0) as usize);

                for index in 0..range_count {
                    let Some(text) = (unsafe { ranges.GetElement(index) })
                        .ok()
                        .and_then(|range| unsafe { range.GetText(-1) }.ok())
                        .map(|text| text.to_string())
                    else {
                        continue;
                    };
                    let text = text.trim();
                    if !text.is_empty() {
                        selections.push(text.to_owned());
                    }
                }

                if !selections.is_empty() {
                    ensure_foreground(expected_foreground)?;
                    return Ok(selections.join("\n"));
                }
            }
        }

        let Ok(parent) = (unsafe { walker.GetParentElement(&element) }) else {
            break;
        };
        element = parent;
    }

    Err(SelectionError::Empty)
}

#[cfg(not(target_os = "windows"))]
pub fn capture_selected_text_automation(
    _expected_foreground: Option<usize>,
) -> Result<String, SelectionError> {
    Err(SelectionError::Automation(
        "UI Automation is only available on Windows".into(),
    ))
}

#[cfg(target_os = "windows")]
fn wait_for_modifiers_release() -> Result<(), SelectionError> {
    use windows_sys::Win32::UI::Input::KeyboardAndMouse::{
        GetAsyncKeyState, VK_CONTROL, VK_LCONTROL, VK_LMENU, VK_LSHIFT, VK_LWIN, VK_MENU,
        VK_RCONTROL, VK_RMENU, VK_RSHIFT, VK_RWIN, VK_SHIFT,
    };

    const MODIFIER_KEYS: [u16; 11] = [
        VK_CONTROL,
        VK_LCONTROL,
        VK_RCONTROL,
        VK_MENU,
        VK_LMENU,
        VK_RMENU,
        VK_SHIFT,
        VK_LSHIFT,
        VK_RSHIFT,
        VK_LWIN,
        VK_RWIN,
    ];
    const CURRENTLY_DOWN: u16 = 0x8000;

    let timeout = Duration::from_millis(1_500);
    let started = std::time::Instant::now();

    loop {
        let any_pressed = MODIFIER_KEYS.iter().any(|key| {
            let state = unsafe { GetAsyncKeyState(i32::from(*key)) } as u16;
            state & CURRENTLY_DOWN != 0
        });

        if !any_pressed {
            // Give the foreground application one message-loop turn to process key-up.
            thread::sleep(Duration::from_millis(30));
            return Ok(());
        }
        if started.elapsed() >= timeout {
            return Err(SelectionError::ModifiersHeld);
        }

        thread::sleep(Duration::from_millis(10));
    }
}

#[cfg(not(target_os = "windows"))]
fn wait_for_modifiers_release() -> Result<(), SelectionError> {
    Ok(())
}

#[cfg(target_os = "windows")]
fn send_copy_shortcut() -> Result<(), SelectionError> {
    use windows_sys::Win32::UI::Input::KeyboardAndMouse::{
        SendInput, INPUT, INPUT_0, INPUT_KEYBOARD, KEYBDINPUT, KEYEVENTF_KEYUP, VK_C, VK_CONTROL,
    };

    let inputs = [
        INPUT {
            r#type: INPUT_KEYBOARD,
            Anonymous: INPUT_0 {
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
            Anonymous: INPUT_0 {
                ki: KEYBDINPUT {
                    wVk: VK_C,
                    wScan: 0,
                    dwFlags: 0,
                    time: 0,
                    dwExtraInfo: 0,
                },
            },
        },
        INPUT {
            r#type: INPUT_KEYBOARD,
            Anonymous: INPUT_0 {
                ki: KEYBDINPUT {
                    wVk: VK_C,
                    wScan: 0,
                    dwFlags: KEYEVENTF_KEYUP,
                    time: 0,
                    dwExtraInfo: 0,
                },
            },
        },
        INPUT {
            r#type: INPUT_KEYBOARD,
            Anonymous: INPUT_0 {
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

    let sent = unsafe {
        SendInput(
            inputs.len() as u32,
            inputs.as_ptr(),
            std::mem::size_of::<INPUT>() as i32,
        )
    };

    if sent == inputs.len() as u32 {
        Ok(())
    } else {
        let cleanup = [
            INPUT {
                r#type: INPUT_KEYBOARD,
                Anonymous: INPUT_0 {
                    ki: KEYBDINPUT {
                        wVk: VK_C,
                        wScan: 0,
                        dwFlags: KEYEVENTF_KEYUP,
                        time: 0,
                        dwExtraInfo: 0,
                    },
                },
            },
            INPUT {
                r#type: INPUT_KEYBOARD,
                Anonymous: INPUT_0 {
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
        unsafe {
            SendInput(
                cleanup.len() as u32,
                cleanup.as_ptr(),
                std::mem::size_of::<INPUT>() as i32,
            );
        }
        Err(SelectionError::Input)
    }
}

#[cfg(not(target_os = "windows"))]
fn send_copy_shortcut() -> Result<(), SelectionError> {
    Err(SelectionError::Input)
}

pub fn capture_selected_text(expected_foreground: Option<usize>) -> Result<String, SelectionError> {
    if let Ok(text) = capture_selected_text_automation(expected_foreground) {
        return Ok(text);
    }

    wait_for_modifiers_release()?;
    ensure_foreground(expected_foreground)?;

    let initial_sequence = ensure_clipboard_is_restorable()?;

    let mut clipboard =
        Clipboard::new().map_err(|error| SelectionError::Clipboard(error.to_string()))?;
    let backup = ClipboardBackup::read(&mut clipboard);
    if clipboard_sequence() != initial_sequence {
        return Err(SelectionError::Clipboard(
            "clipboard changed while preparing capture; try again".into(),
        ));
    }

    clipboard
        .set_text(String::new())
        .map_err(|error| SelectionError::Clipboard(error.to_string()))?;
    let cleared_sequence = clipboard_sequence();

    if let Err(error) = send_copy_shortcut() {
        if clipboard_sequence() == cleared_sequence {
            backup.restore(&mut clipboard)?;
        }
        return Err(error);
    }

    let mut selected = String::new();
    let mut capture_sequence = cleared_sequence;
    for _ in 0..20 {
        thread::sleep(Duration::from_millis(30));
        let sequence = clipboard_sequence();
        if sequence != cleared_sequence {
            capture_sequence = sequence;
        }
        if let Ok(value) = clipboard.get_text() {
            if !value.trim().is_empty() {
                selected = value;
                break;
            }
        }
    }

    // Do not overwrite a copy the user made while capture was completing.
    if clipboard_sequence() == capture_sequence {
        backup.restore(&mut clipboard)?;
    }

    let selected = selected.trim().to_owned();
    if selected.is_empty() {
        Err(SelectionError::Empty)
    } else {
        Ok(selected)
    }
}
