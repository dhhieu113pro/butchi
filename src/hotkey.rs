use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::mpsc::Sender;
use std::time::{SystemTime, UNIX_EPOCH};
use windows_sys::Win32::Foundation::{HINSTANCE, LPARAM, LRESULT, WPARAM};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::Input::KeyboardAndMouse::{
    VK_CONTROL, VK_LCONTROL, VK_RCONTROL,
};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    CallNextHookEx, DispatchMessageW, GetMessageW, SetWindowsHookExW, UnhookWindowsHookEx,
    KBDLLHOOKSTRUCT, MSG, WH_KEYBOARD_LL, WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN, WM_SYSKEYUP,
};

static LAST_CTRL_UP_MS: AtomicU64 = AtomicU64::new(0);
static TIMEOUT_MS: AtomicU64 = AtomicU64::new(500);
static SENDER_PTR: std::sync::Mutex<Option<Sender<()>>> = std::sync::Mutex::new(None);

pub fn set_double_ctrl_timeout(timeout_ms: u64) {
    TIMEOUT_MS.store(timeout_ms, Ordering::Relaxed);
}

fn current_time_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64
}

unsafe extern "system" fn low_level_keyboard_proc(
    n_code: i32,
    w_param: WPARAM,
    l_param: LPARAM,
) -> LRESULT {
    if n_code >= 0 {
        let kbd_struct = *(l_param as *const KBDLLHOOKSTRUCT);
        let vk_code = kbd_struct.vkCode as u16;
        let is_ctrl = vk_code == VK_CONTROL
            || vk_code == VK_LCONTROL
            || vk_code == VK_RCONTROL;

        let msg = w_param as u32;

        if is_ctrl && (msg == WM_KEYUP || msg == WM_SYSKEYUP) {
            let now = current_time_ms();
            let last = LAST_CTRL_UP_MS.swap(now, Ordering::Relaxed);
            let timeout = TIMEOUT_MS.load(Ordering::Relaxed);
            let delta = now.saturating_sub(last);

            if last > 0 && delta <= timeout && delta >= 50 {
                println!("\n🎯 Double-Ctrl detected! (Interval: {}ms)", delta);
                LAST_CTRL_UP_MS.store(0, Ordering::Relaxed);

                if let Ok(guard) = SENDER_PTR.lock() {
                    if let Some(ref tx) = *guard {
                        let _ = tx.send(());
                    }
                }
            } else {
                println!("[Hook] Ctrl released. Waiting for 2nd Ctrl tap... (delta: {}ms)", delta);
            }
        }
    }

    CallNextHookEx(std::ptr::null_mut(), n_code, w_param, l_param)
}

pub fn start_keyboard_hook(tx: Sender<()>, timeout_ms: u64) {
    set_double_ctrl_timeout(timeout_ms);
    if let Ok(mut guard) = SENDER_PTR.lock() {
        *guard = Some(tx);
    }

    std::thread::spawn(move || unsafe {
        let hinstance = GetModuleHandleW(std::ptr::null());
        let hook = SetWindowsHookExW(
            WH_KEYBOARD_LL,
            Some(low_level_keyboard_proc),
            hinstance,
            0,
        );

        if hook.is_null() {
            eprintln!("❌ Failed to install Windows low-level keyboard hook.");
            return;
        }

        println!("✅ Windows Low-Level Keyboard Hook active across all applications!");
        let mut msg: MSG = std::mem::zeroed();
        while GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) > 0 {
            DispatchMessageW(&msg);
        }

        UnhookWindowsHookEx(hook);
    });
}
