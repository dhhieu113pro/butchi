use std::sync::atomic::{AtomicBool, Ordering};
use windows_sys::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM};
use windows_sys::Win32::UI::Shell::{
    Shell_NotifyIconW, NIF_ICON, NIF_MESSAGE, NIF_TIP, NIM_ADD, NIM_DELETE, NOTIFYICONDATAW,
};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    AppendMenuW, CreatePopupMenu, CreateWindowExW, DefWindowProcW, DestroyWindow, DispatchMessageW,
    GetCursorPos, GetMessageW, LoadIconW, RegisterClassW, SetForegroundWindow, TrackPopupMenu,
    IDI_APPLICATION, MF_STRING, TPM_BOTTOMALIGN, TPM_LEFTALIGN, WM_COMMAND, WM_DESTROY,
    WM_RBUTTONUP, WM_USER, WNDCLASSW,
};

const WM_TRAYICON: u32 = WM_USER + 1;
const ID_EXIT: usize = 1001;
const ID_CONFIG: usize = 1002;

static IS_RUNNING: AtomicBool = AtomicBool::new(true);

unsafe extern "system" fn window_proc(
    hwnd: HWND,
    msg: u32,
    w_param: WPARAM,
    l_param: LPARAM,
) -> LRESULT {
    match msg {
        WM_TRAYICON => {
            let l_event = l_param as u32;
            if l_event == WM_RBUTTONUP {
                let mut pt = std::mem::zeroed();
                GetCursorPos(&mut pt);

                let hmenu = CreatePopupMenu();
                let title = encode_wide("🤖 English Grammar Rewriter (Double-Ctrl)");
                let cfg_title = encode_wide("⚙️ Open Config (config.toml)");
                let exit_title = encode_wide("❌ Exit");

                AppendMenuW(hmenu, MF_STRING, 0, title.as_ptr());
                AppendMenuW(hmenu, MF_STRING, ID_CONFIG, cfg_title.as_ptr());
                AppendMenuW(hmenu, MF_STRING, ID_EXIT, exit_title.as_ptr());

                SetForegroundWindow(hwnd);
                TrackPopupMenu(
                    hmenu,
                    TPM_LEFTALIGN | TPM_BOTTOMALIGN,
                    pt.x,
                    pt.y,
                    0,
                    hwnd,
                    std::ptr::null(),
                );
            }
            0
        }
        WM_COMMAND => {
            let id = w_param as usize;
            if id == ID_EXIT {
                println!("Exit requested from System Tray.");
                IS_RUNNING.store(false, Ordering::Relaxed);
                PostQuitMessage(0);
            } else if id == ID_CONFIG {
                let _ = std::process::Command::new("notepad.exe")
                    .arg("config.toml")
                    .spawn();
            }
            0
        }
        WM_DESTROY => {
            PostQuitMessage(0);
            0
        }
        _ => DefWindowProcW(hwnd, msg, w_param, l_param),
    }
}

extern "system" {
    fn PostQuitMessage(nExitCode: i32);
}

fn encode_wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

pub struct WinTray {
    hwnd: HWND,
    nid: NOTIFYICONDATAW,
}

impl WinTray {
    pub fn new(tooltip_text: &str) -> Option<Self> {
        unsafe {
            let class_name = encode_wide("RustRewriteTrayClass");
            let wnd_class = WNDCLASSW {
                style: 0,
                lpfnWndProc: Some(window_proc),
                cbClsExtra: 0,
                cbWndExtra: 0,
                hInstance: std::ptr::null_mut(),
                hIcon: LoadIconW(std::ptr::null_mut(), IDI_APPLICATION),
                hCursor: std::ptr::null_mut(),
                hbrBackground: std::ptr::null_mut(),
                lpszMenuName: std::ptr::null(),
                lpszClassName: class_name.as_ptr(),
            };

            RegisterClassW(&wnd_class);

            let hwnd = CreateWindowExW(
                0,
                class_name.as_ptr(),
                class_name.as_ptr(),
                0,
                0,
                0,
                0,
                0,
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null(),
            );

            if hwnd.is_null() {
                return None;
            }

            let mut nid: NOTIFYICONDATAW = std::mem::zeroed();
            nid.cbSize = std::mem::size_of::<NOTIFYICONDATAW>() as u32;
            nid.hWnd = hwnd;
            nid.uID = 1;
            nid.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
            nid.uCallbackMessage = WM_TRAYICON;
            nid.hIcon = LoadIconW(std::ptr::null_mut(), IDI_APPLICATION);

            let tooltip_wide = encode_wide(tooltip_text);
            let len = tooltip_wide.len().min(nid.szTip.len());
            nid.szTip[..len].copy_from_slice(&tooltip_wide[..len]);

            if Shell_NotifyIconW(NIM_ADD, &nid) == 0 {
                eprintln!("Failed to add icon to System Tray");
                return None;
            }

            println!("✅ System Tray Icon added successfully to Taskbar!");
            Some(Self { hwnd, nid })
        }
    }
}

impl Drop for WinTray {
    fn drop(&mut self) {
        unsafe {
            Shell_NotifyIconW(NIM_DELETE, &self.nid);
            if !self.hwnd.is_null() {
                DestroyWindow(self.hwnd);
            }
        }
    }
}

pub fn run_tray_loop(tooltip: &str) {
    let tooltip = tooltip.to_string();
    std::thread::spawn(move || {
        let _tray = WinTray::new(&tooltip);
        unsafe {
            let mut msg = std::mem::zeroed();
            while GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) > 0 {
                DispatchMessageW(&msg);
            }
        }
        std::process::exit(0);
    });
}
