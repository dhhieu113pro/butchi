use windows_sys::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::Shell::{
    Shell_NotifyIconW, NIF_ICON, NIF_MESSAGE, NIF_TIP, NIM_ADD, NIM_DELETE, NOTIFYICONDATAW,
};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    AppendMenuW, CreatePopupMenu, CreateWindowExW, DefWindowProcW, DispatchMessageW,
    GetCursorPos, GetMessageW, LoadImageW, PostMessageW, RegisterClassW, SetForegroundWindow,
    TrackPopupMenu, IDI_APPLICATION, IMAGE_ICON, LR_DEFAULTSIZE, LR_SHARED, MF_STRING,
    TPM_BOTTOMALIGN, TPM_LEFTALIGN, WM_COMMAND, WM_CREATE, WM_DESTROY, WM_RBUTTONUP, WM_USER,
    WNDCLASSW,
};

const WM_TRAYICON: u32 = WM_USER + 1;
const WM_INIT_TRAY: u32 = WM_USER + 2;
const ID_EXIT: usize = 1001;
const ID_CONFIG: usize = 1002;

static mut GLOBAL_NID: Option<NOTIFYICONDATAW> = None;
static mut TOOLTIP_TEXT: String = String::new();

unsafe extern "system" fn window_proc(
    hwnd: HWND,
    msg: u32,
    w_param: WPARAM,
    l_param: LPARAM,
) -> LRESULT {
    match msg {
        WM_CREATE => {
            PostMessageW(hwnd, WM_INIT_TRAY, 0, 0);
            0
        }
        WM_INIT_TRAY => {
            let icon_handle = LoadImageW(
                std::ptr::null_mut(),
                IDI_APPLICATION,
                IMAGE_ICON,
                0,
                0,
                LR_SHARED | LR_DEFAULTSIZE,
            );

            let mut nid: NOTIFYICONDATAW = std::mem::zeroed();
            nid.cbSize = std::mem::size_of::<NOTIFYICONDATAW>() as u32;
            nid.hWnd = hwnd;
            nid.uID = 100;
            nid.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
            nid.uCallbackMessage = WM_TRAYICON;
            nid.hIcon = icon_handle;

            let text_ref = &raw const TOOLTIP_TEXT;
            let tooltip_wide = encode_wide(&*text_ref);
            let len = tooltip_wide.len().min(nid.szTip.len());
            nid.szTip[..len].copy_from_slice(&tooltip_wide[..len]);

            if Shell_NotifyIconW(NIM_ADD, &nid) != 0 {
                println!("✅ System Tray Icon created and added to Taskbar!");
                GLOBAL_NID = Some(nid);
            }
            0
        }
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
                if let Some(ref nid) = GLOBAL_NID {
                    Shell_NotifyIconW(NIM_DELETE, nid);
                }
                PostQuitMessage(0);
            } else if id == ID_CONFIG {
                let _ = std::process::Command::new("notepad.exe")
                    .arg("config.toml")
                    .spawn();
            }
            0
        }
        WM_DESTROY => {
            if let Some(ref nid) = GLOBAL_NID {
                Shell_NotifyIconW(NIM_DELETE, nid);
            }
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

pub fn run_tray_loop(tooltip: &str) {
    unsafe {
        TOOLTIP_TEXT = tooltip.to_string();
    }
    std::thread::spawn(move || unsafe {
        let class_name = encode_wide("RustRewriteTrayClass");
        let hinstance = GetModuleHandleW(std::ptr::null());

        let wnd_class = WNDCLASSW {
            style: 0,
            lpfnWndProc: Some(window_proc),
            cbClsExtra: 0,
            cbWndExtra: 0,
            hInstance: hinstance,
            hIcon: LoadImageW(
                std::ptr::null_mut(),
                IDI_APPLICATION,
                IMAGE_ICON,
                0,
                0,
                LR_SHARED | LR_DEFAULTSIZE,
            ),
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
            hinstance,
            std::ptr::null(),
        );

        if hwnd.is_null() {
            eprintln!("Failed creating hidden tray window.");
            return;
        }

        let mut msg = std::mem::zeroed();
        while GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) > 0 {
            DispatchMessageW(&msg);
        }
        std::process::exit(0);
    });
}
