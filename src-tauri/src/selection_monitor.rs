#[cfg(target_os = "windows")]
mod windows {
    use std::{
        io,
        sync::{mpsc, OnceLock},
        thread,
        time::Duration,
    };

    use tauri::{AppHandle, Manager, PhysicalPosition};
    use windows_sys::Win32::{
        Foundation::{LPARAM, LRESULT, POINT as WinPoint, WPARAM},
        System::LibraryLoader::GetModuleHandleW,
        UI::{
            Input::KeyboardAndMouse::{GetAsyncKeyState, GetDoubleClickTime, VK_SHIFT},
            WindowsAndMessaging::{
                CallNextHookEx, DispatchMessageW, GetForegroundWindow, GetMessageW,
                GetSystemMetrics, SetWindowsHookExW, TranslateMessage, UnhookWindowsHookEx,
                WindowFromPoint, GA_ROOT, HC_ACTION, LLMHF_INJECTED, MSG, MSLLHOOKSTRUCT,
                SM_CXDOUBLECLK, SM_CXDRAG, SM_CYDOUBLECLK, SM_CYDRAG, WH_MOUSE_LL, WM_LBUTTONDOWN,
                WM_LBUTTONUP,
            },
        },
    };

    const CAPTURE_DELAY: Duration = Duration::from_millis(120);
    const EVENT_QUEUE_SIZE: usize = 64;
    const KEY_DOWN: u16 = 0x8000;

    static EVENT_SENDER: OnceLock<mpsc::SyncSender<MouseEvent>> = OnceLock::new();

    #[derive(Clone, Copy, Debug, PartialEq, Eq)]
    struct Point {
        x: i32,
        y: i32,
    }

    #[derive(Clone, Copy, Debug)]
    enum MouseEventKind {
        Down,
        Up,
    }

    #[derive(Clone, Copy, Debug)]
    struct MouseEvent {
        kind: MouseEventKind,
        point: Point,
        time: u32,
        source_window: usize,
        shift_down: bool,
    }

    #[derive(Clone, Copy, Debug)]
    struct Candidate {
        point: Point,
        source_window: usize,
    }

    #[derive(Clone, Copy, Debug)]
    struct Press {
        point: Point,
        source_window: usize,
    }

    #[derive(Clone, Copy, Debug)]
    struct Click {
        point: Point,
        time: u32,
        source_window: usize,
    }

    struct GestureDetector {
        drag_x: u32,
        drag_y: u32,
        double_click_x: u32,
        double_click_y: u32,
        double_click_ms: u32,
        press: Option<Press>,
        previous_click: Option<Click>,
    }

    impl GestureDetector {
        fn from_system() -> Self {
            let metric = |index| unsafe { GetSystemMetrics(index) }.unsigned_abs().max(1);

            Self {
                drag_x: metric(SM_CXDRAG),
                drag_y: metric(SM_CYDRAG),
                double_click_x: metric(SM_CXDOUBLECLK),
                double_click_y: metric(SM_CYDOUBLECLK),
                double_click_ms: unsafe { GetDoubleClickTime() }.max(1),
                press: None,
                previous_click: None,
            }
        }

        #[cfg(test)]
        fn fixed() -> Self {
            Self {
                drag_x: 4,
                drag_y: 4,
                double_click_x: 4,
                double_click_y: 4,
                double_click_ms: 500,
                press: None,
                previous_click: None,
            }
        }

        fn process(&mut self, event: MouseEvent) -> Option<Candidate> {
            match event.kind {
                MouseEventKind::Down => {
                    self.press = Some(Press {
                        point: event.point,
                        source_window: event.source_window,
                    });
                    None
                }
                MouseEventKind::Up => self.process_up(event),
            }
        }

        fn process_up(&mut self, event: MouseEvent) -> Option<Candidate> {
            let press = self.press.take()?;
            if press.source_window == 0 {
                self.previous_click = None;
                return None;
            }

            let moved_x = press.point.x.abs_diff(event.point.x);
            let moved_y = press.point.y.abs_diff(event.point.y);
            let is_drag = moved_x >= self.drag_x || moved_y >= self.drag_y;

            if is_drag {
                self.previous_click = None;
                return Some(Candidate {
                    point: event.point,
                    source_window: press.source_window,
                });
            }

            if press.source_window != event.source_window {
                self.previous_click = None;
                return None;
            }

            let is_double_click = self.previous_click.is_some_and(|click| {
                click.source_window == event.source_window
                    && event.time.wrapping_sub(click.time) <= self.double_click_ms
                    && click.point.x.abs_diff(event.point.x) <= self.double_click_x
                    && click.point.y.abs_diff(event.point.y) <= self.double_click_y
            });

            self.previous_click = Some(Click {
                point: event.point,
                time: event.time,
                source_window: event.source_window,
            });

            if is_double_click || event.shift_down {
                self.previous_click = None;
                Some(Candidate {
                    point: event.point,
                    source_window: press.source_window,
                })
            } else {
                None
            }
        }
    }

    pub fn start(app: AppHandle) -> io::Result<()> {
        let (event_sender, event_receiver) = mpsc::sync_channel(EVENT_QUEUE_SIZE);
        EVENT_SENDER.set(event_sender).map_err(|_| {
            io::Error::new(
                io::ErrorKind::AlreadyExists,
                "selection monitor has already started",
            )
        })?;

        thread::Builder::new()
            .name("selection-candidate-worker".into())
            .spawn(move || run_worker(app, event_receiver))?;

        let (ready_sender, ready_receiver) = mpsc::sync_channel(1);
        thread::Builder::new()
            .name("selection-mouse-hook".into())
            .spawn(move || run_hook(ready_sender))?;

        ready_receiver
            .recv_timeout(Duration::from_secs(2))
            .map_err(|_| io::Error::new(io::ErrorKind::TimedOut, "mouse hook did not start"))?
    }

    fn run_worker(app: AppHandle, events: mpsc::Receiver<MouseEvent>) {
        let mut detector = GestureDetector::from_system();

        while let Ok(event) = events.recv() {
            let Some(candidate) = detector.process(event) else {
                continue;
            };

            if point_is_inside_popover(&app, candidate.point) {
                continue;
            }

            let foreground = unsafe { GetForegroundWindow() } as usize;
            if foreground == 0 || foreground != candidate.source_window {
                continue;
            }

            crate::popover::capture_and_show_near(
                app.clone(),
                PhysicalPosition::new(candidate.point.x, candidate.point.y),
                CAPTURE_DELAY,
                candidate.source_window,
            );
        }
    }

    fn point_is_inside_popover(app: &AppHandle, point: Point) -> bool {
        let Some(window) = app.get_webview_window("popover") else {
            return false;
        };
        if !window.is_visible().unwrap_or(false) {
            return false;
        }

        let Ok(position) = window.outer_position() else {
            return false;
        };
        let Ok(size) = window.outer_size() else {
            return false;
        };

        point.x >= position.x
            && point.y >= position.y
            && point.x < position.x + size.width as i32
            && point.y < position.y + size.height as i32
    }

    fn run_hook(ready: mpsc::SyncSender<io::Result<()>>) {
        let module = unsafe { GetModuleHandleW(std::ptr::null()) };
        let hook = unsafe { SetWindowsHookExW(WH_MOUSE_LL, Some(mouse_hook), module, 0) };

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

    unsafe extern "system" fn mouse_hook(code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
        if code >= HC_ACTION as i32 {
            let kind = match wparam as u32 {
                WM_LBUTTONDOWN => Some(MouseEventKind::Down),
                WM_LBUTTONUP => Some(MouseEventKind::Up),
                _ => None,
            };

            if let Some(kind) = kind {
                let data = unsafe { &*(lparam as *const MSLLHOOKSTRUCT) };
                if data.flags & LLMHF_INJECTED == 0 {
                    let shift_state = unsafe { GetAsyncKeyState(i32::from(VK_SHIFT)) } as u16;
                    let event = MouseEvent {
                        kind,
                        point: Point {
                            x: data.pt.x,
                            y: data.pt.y,
                        },
                        time: data.time,
                        source_window: root_window_at(data.pt.x, data.pt.y),
                        shift_down: shift_state & KEY_DOWN != 0,
                    };

                    if let Some(sender) = EVENT_SENDER.get() {
                        let _ = sender.try_send(event);
                    }
                }
            }
        }

        unsafe { CallNextHookEx(std::ptr::null_mut(), code, wparam, lparam) }
    }

    fn root_window_at(x: i32, y: i32) -> usize {
        use windows_sys::Win32::UI::WindowsAndMessaging::GetAncestor;

        let child = unsafe { WindowFromPoint(WinPoint { x, y }) };
        if child.is_null() {
            return 0;
        }

        let root = unsafe { GetAncestor(child, GA_ROOT) };
        if root.is_null() {
            child as usize
        } else {
            root as usize
        }
    }

    #[cfg(test)]
    mod tests {
        use super::*;

        fn event(kind: MouseEventKind, x: i32, y: i32, time: u32) -> MouseEvent {
            event_for_window(kind, x, y, time, 7)
        }

        fn event_for_window(
            kind: MouseEventKind,
            x: i32,
            y: i32,
            time: u32,
            source_window: usize,
        ) -> MouseEvent {
            MouseEvent {
                kind,
                point: Point { x, y },
                time,
                source_window,
                shift_down: false,
            }
        }

        #[test]
        fn drag_emits_one_candidate_on_mouse_up() {
            let mut detector = GestureDetector::fixed();
            assert!(detector
                .process(event(MouseEventKind::Down, 10, 10, 1))
                .is_none());
            assert!(detector
                .process(event(MouseEventKind::Up, 20, 10, 100))
                .is_some());
        }

        #[test]
        fn single_click_is_ignored_but_double_click_emits() {
            let mut detector = GestureDetector::fixed();
            assert!(detector
                .process(event(MouseEventKind::Down, 10, 10, 1))
                .is_none());
            assert!(detector
                .process(event(MouseEventKind::Up, 10, 10, 20))
                .is_none());
            assert!(detector
                .process(event(MouseEventKind::Down, 11, 10, 100))
                .is_none());
            assert!(detector
                .process(event(MouseEventKind::Up, 11, 10, 120))
                .is_some());
        }

        #[test]
        fn slow_second_click_is_not_a_double_click() {
            let mut detector = GestureDetector::fixed();
            detector.process(event(MouseEventKind::Down, 10, 10, 1));
            detector.process(event(MouseEventKind::Up, 10, 10, 20));
            detector.process(event(MouseEventKind::Down, 10, 10, 600));
            assert!(detector
                .process(event(MouseEventKind::Up, 10, 10, 620))
                .is_none());
        }

        #[test]
        fn drag_keeps_the_window_where_the_gesture_started() {
            let mut detector = GestureDetector::fixed();
            detector.process(event_for_window(MouseEventKind::Down, 10, 10, 1, 7));
            let candidate = detector
                .process(event_for_window(MouseEventKind::Up, 20, 10, 100, 8))
                .expect("drag should be accepted even when released outside the source window");
            assert_eq!(candidate.source_window, 7);
        }

        #[test]
        fn click_crossing_windows_is_ignored() {
            let mut detector = GestureDetector::fixed();
            detector.process(event_for_window(MouseEventKind::Down, 10, 10, 1, 7));
            assert!(detector
                .process(event_for_window(MouseEventKind::Up, 10, 10, 20, 8))
                .is_none());
        }
    }
}

#[cfg(target_os = "windows")]
pub use windows::start;

#[cfg(not(target_os = "windows"))]
pub fn start(_app: tauri::AppHandle) -> std::io::Result<()> {
    Ok(())
}
