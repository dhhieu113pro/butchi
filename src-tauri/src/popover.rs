use std::{
    sync::atomic::{AtomicBool, Ordering},
    time::Duration,
};

use tauri::{AppHandle, Emitter, LogicalSize, Manager, PhysicalPosition};

const WINDOW_LABEL: &str = "popover";
const CURSOR_GAP: i32 = 12;
static CAPTURE_ACTIVE: AtomicBool = AtomicBool::new(false);

#[derive(Clone, Copy)]
enum FailureAction {
    Silent,
    Report,
    RequestInput,
}

#[derive(Clone, Copy)]
enum PopoverMode {
    Selection,
    Input,
}

fn place_near_cursor(
    cursor: PhysicalPosition<i32>,
    window_size: (i32, i32),
    monitor_position: PhysicalPosition<i32>,
    monitor_size: (i32, i32),
) -> PhysicalPosition<i32> {
    let right = monitor_position.x + monitor_size.0;
    let bottom = monitor_position.y + monitor_size.1;
    let mut x = cursor.x + CURSOR_GAP;
    let mut y = cursor.y + CURSOR_GAP;

    if x + window_size.0 > right {
        x = cursor.x - window_size.0 - CURSOR_GAP;
    }
    if y + window_size.1 > bottom {
        y = cursor.y - window_size.1 - CURSOR_GAP;
    }

    PhysicalPosition::new(x.max(monitor_position.x), y.max(monitor_position.y))
}

#[cfg(target_os = "windows")]
fn cursor_position() -> Option<PhysicalPosition<i32>> {
    use windows_sys::Win32::{Foundation::POINT, UI::WindowsAndMessaging::GetCursorPos};

    let mut point = POINT { x: 0, y: 0 };
    if unsafe { GetCursorPos(&mut point) } == 0 {
        None
    } else {
        Some(PhysicalPosition::new(point.x, point.y))
    }
}

#[cfg(not(target_os = "windows"))]
fn cursor_position() -> Option<PhysicalPosition<i32>> {
    None
}

#[cfg(target_os = "windows")]
fn foreground_window() -> usize {
    use windows_sys::Win32::UI::WindowsAndMessaging::GetForegroundWindow;

    (unsafe { GetForegroundWindow() }) as usize
}

#[cfg(not(target_os = "windows"))]
fn foreground_window() -> usize {
    0
}

fn show_event_at(
    app: &AppHandle,
    event: &str,
    payload: String,
    anchor: Option<PhysicalPosition<i32>>,
    mode: PopoverMode,
) -> tauri::Result<()> {
    let Some(window) = app.get_webview_window(WINDOW_LABEL) else {
        return Ok(());
    };

    let (width, height) = match mode {
        PopoverMode::Selection => (380.0, 220.0),
        PopoverMode::Input => (380.0, 200.0),
    };
    window.set_size(LogicalSize::new(width, height))?;

    if let Some(cursor) = anchor.or_else(cursor_position) {
        let size = window.outer_size()?;
        let mut position = PhysicalPosition::new(cursor.x + CURSOR_GAP, cursor.y + CURSOR_GAP);

        if let Some(monitor) = window.available_monitors()?.into_iter().find(|monitor| {
            let position = monitor.position();
            let size = monitor.size();
            cursor.x >= position.x
                && cursor.y >= position.y
                && cursor.x < position.x + size.width as i32
                && cursor.y < position.y + size.height as i32
        }) {
            position = place_near_cursor(
                cursor,
                (size.width as i32, size.height as i32),
                *monitor.position(),
                (monitor.size().width as i32, monitor.size().height as i32),
            );
        }

        window.set_position(position)?;
    }

    window.emit(event, payload)?;
    match mode {
        PopoverMode::Selection => {
            window.set_focusable(false)?;
            window.show()?;
        }
        PopoverMode::Input => {
            window.set_focusable(true)?;
            window.show()?;
            window.set_focus()?;
        }
    }
    Ok(())
}

#[allow(dead_code)]
pub fn capture_and_show(app: AppHandle) {
    let source_window = foreground_window();
    let expected_foreground = (source_window != 0).then_some(source_window);
    start_capture(
        app,
        None,
        Duration::ZERO,
        FailureAction::Report,
        expected_foreground,
        false,
    );
}

pub fn capture_or_request_input(app: AppHandle) {
    let source_window = foreground_window();
    let expected_foreground = (source_window != 0).then_some(source_window);
    start_capture(
        app,
        None,
        Duration::ZERO,
        FailureAction::RequestInput,
        expected_foreground,
        false,
    );
}

pub fn capture_and_show_near(
    app: AppHandle,
    anchor: PhysicalPosition<i32>,
    delay: Duration,
    source_window: usize,
) {
    start_capture(
        app,
        Some(anchor),
        delay,
        FailureAction::Silent,
        Some(source_window),
        true,
    );
}

fn start_capture(
    app: AppHandle,
    anchor: Option<PhysicalPosition<i32>>,
    delay: Duration,
    failure_action: FailureAction,
    expected_foreground: Option<usize>,
    automation_only: bool,
) {
    if CAPTURE_ACTIVE
        .compare_exchange(false, true, Ordering::AcqRel, Ordering::Acquire)
        .is_err()
    {
        return;
    }

    std::thread::spawn(move || {
        if !delay.is_zero() {
            std::thread::sleep(delay);
        }

        if expected_foreground.is_some_and(|window| foreground_window() != window) {
            CAPTURE_ACTIVE.store(false, Ordering::Release);
            return;
        }

        let capture = if automation_only {
            crate::selection::capture_selected_text_automation(expected_foreground)
        } else {
            crate::selection::capture_selected_text(expected_foreground)
        };

        match capture {
            Ok(text) => {
                #[cfg(debug_assertions)]
                eprintln!("selection captured: {} characters", text.chars().count());
                if let Err(error) = show_event_at(
                    &app,
                    "selection-captured",
                    text,
                    anchor,
                    PopoverMode::Selection,
                ) {
                    eprintln!("failed to show selection popover: {error}");
                }
            }
            Err(error) => {
                eprintln!("selection capture skipped: {error}");
                match failure_action {
                    FailureAction::Silent => {}
                    FailureAction::Report => {
                        let _ = show_event_at(
                            &app,
                            "selection-capture-failed",
                            error.to_string(),
                            anchor,
                            PopoverMode::Selection,
                        );
                    }
                    FailureAction::RequestInput => {
                        let _ = show_event_at(
                            &app,
                            "manual-input-requested",
                            error.to_string(),
                            anchor,
                            PopoverMode::Input,
                        );
                    }
                }
            }
        }

        CAPTURE_ACTIVE.store(false, Ordering::Release);
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn positions_below_and_right_when_space_is_available() {
        assert_eq!(
            place_near_cursor(
                PhysicalPosition::new(100, 100),
                (360, 124),
                PhysicalPosition::new(0, 0),
                (1920, 1080),
            ),
            PhysicalPosition::new(112, 112)
        );
    }

    #[test]
    fn flips_away_from_bottom_right_edge() {
        assert_eq!(
            place_near_cursor(
                PhysicalPosition::new(1900, 1060),
                (360, 124),
                PhysicalPosition::new(0, 0),
                (1920, 1080),
            ),
            PhysicalPosition::new(1528, 924)
        );
    }
}
