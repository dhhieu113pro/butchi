#[cfg(target_os = "windows")]
mod windows {
    use std::{
        io,
        sync::{mpsc, OnceLock},
        thread,
        time::Duration,
    };

    use tauri::AppHandle;
    use windows_sys::Win32::{
        Foundation::{LPARAM, LRESULT, WPARAM},
        System::LibraryLoader::GetModuleHandleW,
        UI::{
            Input::KeyboardAndMouse::{VK_CONTROL, VK_LCONTROL, VK_RCONTROL},
            WindowsAndMessaging::{
                CallNextHookEx, DispatchMessageW, GetMessageW, SetWindowsHookExW, TranslateMessage,
                UnhookWindowsHookEx, HC_ACTION, KBDLLHOOKSTRUCT, LLKHF_INJECTED, MSG,
                WH_KEYBOARD_LL, WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN, WM_SYSKEYUP,
            },
        },
    };

    const DOUBLE_CTRL_MS: u32 = 350;
    const EVENT_QUEUE_SIZE: usize = 32;
    static EVENT_SENDER: OnceLock<mpsc::SyncSender<KeyEvent>> = OnceLock::new();

    #[derive(Clone, Copy, Debug, PartialEq, Eq)]
    enum KeyEventKind {
        Down,
        Up,
    }

    #[derive(Clone, Copy, Debug)]
    struct KeyEvent {
        kind: KeyEventKind,
        key: u32,
        time: u32,
    }

    #[derive(Default)]
    struct DoubleCtrlDetector {
        held_ctrl: u8,
        chorded: bool,
        previous_tap: Option<u32>,
    }

    impl DoubleCtrlDetector {
        fn process(&mut self, event: KeyEvent) -> bool {
            let Some(ctrl_bit) = ctrl_bit(event.key) else {
                if event.kind == KeyEventKind::Down {
                    self.previous_tap = None;
                    if self.held_ctrl != 0 {
                        self.chorded = true;
                    }
                }
                return false;
            };

            match event.kind {
                KeyEventKind::Down => {
                    if self.held_ctrl & ctrl_bit == 0 {
                        if self.held_ctrl == 0 {
                            self.chorded = false;
                        }
                        self.held_ctrl |= ctrl_bit;
                    }
                    false
                }
                KeyEventKind::Up => {
                    if self.held_ctrl & ctrl_bit == 0 {
                        return false;
                    }
                    self.held_ctrl &= !ctrl_bit;
                    if self.held_ctrl != 0 {
                        return false;
                    }
                    if self.chorded {
                        self.chorded = false;
                        self.previous_tap = None;
                        return false;
                    }

                    let triggered = self.previous_tap.is_some_and(|previous| {
                        event.time.wrapping_sub(previous) <= DOUBLE_CTRL_MS
                    });
                    self.previous_tap = if triggered { None } else { Some(event.time) };
                    triggered
                }
            }
        }
    }

    fn ctrl_bit(key: u32) -> Option<u8> {
        match key {
            key if key == u32::from(VK_CONTROL) || key == u32::from(VK_LCONTROL) => Some(1),
            key if key == u32::from(VK_RCONTROL) => Some(2),
            _ => None,
        }
    }

    pub fn start(app: AppHandle) -> io::Result<()> {
        let (event_sender, event_receiver) = mpsc::sync_channel(EVENT_QUEUE_SIZE);
        EVENT_SENDER.set(event_sender).map_err(|_| {
            io::Error::new(
                io::ErrorKind::AlreadyExists,
                "keyboard monitor has already started",
            )
        })?;

        thread::Builder::new()
            .name("double-ctrl-worker".into())
            .spawn(move || run_worker(app, event_receiver))?;

        let (ready_sender, ready_receiver) = mpsc::sync_channel(1);
        thread::Builder::new()
            .name("double-ctrl-hook".into())
            .spawn(move || run_hook(ready_sender))?;

        ready_receiver
            .recv_timeout(Duration::from_secs(2))
            .map_err(|_| io::Error::new(io::ErrorKind::TimedOut, "keyboard hook did not start"))?
    }

    fn run_worker(app: AppHandle, events: mpsc::Receiver<KeyEvent>) {
        let mut detector = DoubleCtrlDetector::default();
        while let Ok(event) = events.recv() {
            if detector.process(event) {
                crate::popover::capture_or_request_input(app.clone());
            }
        }
    }

    fn run_hook(ready: mpsc::SyncSender<io::Result<()>>) {
        let module = unsafe { GetModuleHandleW(std::ptr::null()) };
        let hook = unsafe { SetWindowsHookExW(WH_KEYBOARD_LL, Some(keyboard_hook), module, 0) };

        if hook.is_null() {
            let _ = ready.send(Err(io::Error::last_os_error()));
            return;
        }
        let _ = ready.send(Ok(()));

        let mut message = MSG::default();
        loop {
            let result = unsafe { GetMessageW(&mut message, std::ptr::null_mut(), 0, 0) };
            if result <= 0 {
                break;
            }
            unsafe {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }

        unsafe {
            UnhookWindowsHookEx(hook);
        }
    }

    unsafe extern "system" fn keyboard_hook(code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
        if code >= HC_ACTION as i32 {
            let kind = match wparam as u32 {
                WM_KEYDOWN | WM_SYSKEYDOWN => Some(KeyEventKind::Down),
                WM_KEYUP | WM_SYSKEYUP => Some(KeyEventKind::Up),
                _ => None,
            };

            if let Some(kind) = kind {
                let data = unsafe { &*(lparam as *const KBDLLHOOKSTRUCT) };
                if data.flags & LLKHF_INJECTED == 0 {
                    if let Some(sender) = EVENT_SENDER.get() {
                        let _ = sender.try_send(KeyEvent {
                            kind,
                            key: data.vkCode,
                            time: data.time,
                        });
                    }
                }
            }
        }

        unsafe { CallNextHookEx(std::ptr::null_mut(), code, wparam, lparam) }
    }

    #[cfg(test)]
    mod tests {
        use super::*;

        fn event(kind: KeyEventKind, key: u16, time: u32) -> KeyEvent {
            KeyEvent {
                kind,
                key: u32::from(key),
                time,
            }
        }

        #[test]
        fn two_ctrl_taps_trigger() {
            let mut detector = DoubleCtrlDetector::default();
            assert!(!detector.process(event(KeyEventKind::Down, VK_LCONTROL, 10)));
            assert!(!detector.process(event(KeyEventKind::Up, VK_LCONTROL, 20)));
            assert!(!detector.process(event(KeyEventKind::Down, VK_LCONTROL, 200)));
            assert!(detector.process(event(KeyEventKind::Up, VK_LCONTROL, 220)));
        }

        #[test]
        fn ctrl_chord_is_not_a_tap() {
            let mut detector = DoubleCtrlDetector::default();
            detector.process(event(KeyEventKind::Down, VK_LCONTROL, 10));
            detector.process(event(KeyEventKind::Down, b'C' as u16, 20));
            detector.process(event(KeyEventKind::Up, b'C' as u16, 30));
            detector.process(event(KeyEventKind::Up, VK_LCONTROL, 40));
            detector.process(event(KeyEventKind::Down, VK_LCONTROL, 100));
            assert!(!detector.process(event(KeyEventKind::Up, VK_LCONTROL, 120)));
        }

        #[test]
        fn slow_taps_do_not_trigger() {
            let mut detector = DoubleCtrlDetector::default();
            detector.process(event(KeyEventKind::Down, VK_LCONTROL, 10));
            detector.process(event(KeyEventKind::Up, VK_LCONTROL, 20));
            detector.process(event(KeyEventKind::Down, VK_LCONTROL, 500));
            assert!(!detector.process(event(KeyEventKind::Up, VK_LCONTROL, 520)));
        }
    }
}

#[cfg(target_os = "windows")]
pub use windows::start;

#[cfg(not(target_os = "windows"))]
pub fn start(_app: tauri::AppHandle) -> std::io::Result<()> {
    Ok(())
}
