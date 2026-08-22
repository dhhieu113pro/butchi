use std::fs;
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};

use serde::{Deserialize, Serialize};

use crate::actions::TextAction;
use crate::config;

const HISTORY_FILE: &str = "history.json";
const MAX_ENTRIES: usize = 200;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HistoryEntry {
    pub id: String,
    /// Unix timestamp in milliseconds.
    pub ts: u64,
    pub action: String,
    pub source: String,
    pub result: String,
    pub message: String,
}

fn history_path() -> Result<PathBuf, String> {
    Ok(config::app_data_dir()?.join(HISTORY_FILE))
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

pub fn load_all() -> Vec<HistoryEntry> {
    let Ok(path) = history_path() else {
        return Vec::new();
    };
    match fs::read_to_string(&path) {
        Ok(raw) => serde_json::from_str(&raw).unwrap_or_default(),
        Err(_) => Vec::new(),
    }
}

fn save_all(entries: &[HistoryEntry]) -> Result<(), String> {
    let path = history_path()?;
    let raw = serde_json::to_string_pretty(entries).map_err(|e| e.to_string())?;
    fs::write(&path, raw).map_err(|e| format!("write history: {e}"))
}

/// Prepend a successful process result. Newest first. Caps at MAX_ENTRIES.
pub fn append(action: TextAction, source: &str, result: &str, message: &str) {
    let action_name = match action {
        TextAction::Translate => "translate",
        TextAction::Rewrite => "rewrite",
    };
    let entry = HistoryEntry {
        id: format!("{}-{}", now_ms(), action_name),
        ts: now_ms(),
        action: action_name.into(),
        source: truncate(source, 4000),
        result: truncate(result, 8000),
        message: truncate(message, 500),
    };

    let mut entries = load_all();
    entries.insert(0, entry);
    if entries.len() > MAX_ENTRIES {
        entries.truncate(MAX_ENTRIES);
    }
    let _ = save_all(&entries);
}

pub fn list(limit: Option<usize>) -> Vec<HistoryEntry> {
    let mut entries = load_all();
    let lim = limit.unwrap_or(MAX_ENTRIES).min(MAX_ENTRIES);
    if entries.len() > lim {
        entries.truncate(lim);
    }
    entries
}

pub fn clear() -> Result<(), String> {
    save_all(&[])
}

fn truncate(s: &str, max_chars: usize) -> String {
    let count = s.chars().count();
    if count <= max_chars {
        return s.to_owned();
    }
    s.chars().take(max_chars).collect::<String>() + "…"
}
