use std::fs;
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};

use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};

use crate::actions::TextAction;
use crate::config;

const DB_FILE: &str = "history.db";
const LEGACY_HISTORY_FILE: &str = "history.json";
const MAX_RESULTS: usize = 500;

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
    #[serde(default)]
    pub target_language: Option<String>,
}

fn db_path() -> Result<PathBuf, String> {
    Ok(config::app_data_dir()?.join(DB_FILE))
}

fn legacy_path() -> Result<PathBuf, String> {
    Ok(config::app_data_dir()?.join(LEGACY_HISTORY_FILE))
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

fn open_db() -> Result<Connection, String> {
    let conn = Connection::open(db_path()?).map_err(|e| format!("open history database: {e}"))?;
    conn.execute_batch(
        "PRAGMA journal_mode=WAL;
         CREATE TABLE IF NOT EXISTS history (
           id TEXT PRIMARY KEY,
           ts INTEGER NOT NULL,
           action TEXT NOT NULL,
           source TEXT NOT NULL,
           result TEXT NOT NULL,
           message TEXT NOT NULL,
           target_language TEXT NULL
         );
         CREATE INDEX IF NOT EXISTS idx_history_ts ON history(ts DESC);
         CREATE INDEX IF NOT EXISTS idx_history_action ON history(action);",
    )
    .map_err(|e| format!("initialize history database: {e}"))?;
    migrate_legacy_json(&conn)?;
    Ok(conn)
}

fn migrate_legacy_json(conn: &Connection) -> Result<(), String> {
    let path = legacy_path()?;
    if !path.is_file() {
        return Ok(());
    }

    let raw = fs::read_to_string(&path).map_err(|e| format!("read legacy history: {e}"))?;
    let entries: Vec<HistoryEntry> = serde_json::from_str(&raw).unwrap_or_default();
    for e in entries {
        conn.execute(
            "INSERT OR IGNORE INTO history (id, ts, action, source, result, message, target_language)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)",
            params![e.id, e.ts as i64, e.action, e.source, e.result, e.message, e.target_language],
        )
        .map_err(|err| format!("migrate history row: {err}"))?;
    }

    let migrated = path.with_extension("migrated.json");
    let _ = fs::rename(path, migrated);
    Ok(())
}

fn cleanup_retention(conn: &Connection, retention_days: i32) -> Result<(), String> {
    if retention_days < 0 {
        return Ok(());
    }
    if retention_days == 0 {
        conn.execute("DELETE FROM history", [])
            .map_err(|e| format!("clear disabled history: {e}"))?;
        return Ok(());
    }

    let cutoff = now_ms().saturating_sub(retention_days as u64 * 86_400_000);
    conn.execute("DELETE FROM history WHERE ts < ?1", params![cutoff as i64])
        .map_err(|e| format!("prune history: {e}"))?;
    Ok(())
}

pub fn append(
    action: TextAction,
    source: &str,
    result: &str,
    message: &str,
    target_language: Option<&str>,
) {
    let cfg = config::load();
    if cfg.history_retention_days == 0 {
        return;
    }

    let action_name = match action {
        TextAction::Translate => "translate",
        TextAction::Rewrite => "rewrite",
    };
    let ts = now_ms();
    let entry = HistoryEntry {
        id: format!("{ts}-{action_name}"),
        ts,
        action: action_name.into(),
        source: truncate(source, 8000),
        result: truncate(result, 16000),
        message: truncate(message, 1000),
        target_language: target_language.map(|s| truncate(s, 100)),
    };

    if let Ok(conn) = open_db() {
        let _ = cleanup_retention(&conn, cfg.history_retention_days);
        let _ = conn.execute(
            "INSERT OR REPLACE INTO history (id, ts, action, source, result, message, target_language)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)",
            params![
                entry.id,
                entry.ts as i64,
                entry.action,
                entry.source,
                entry.result,
                entry.message,
                entry.target_language
            ],
        );
    }
}

pub fn search(
    query: Option<&str>,
    action: Option<&str>,
    limit: Option<usize>,
) -> Result<Vec<HistoryEntry>, String> {
    let conn = open_db()?;
    let cfg = config::load();
    cleanup_retention(&conn, cfg.history_retention_days)?;

    let q = query.unwrap_or("").trim().to_lowercase();
    let action = action.unwrap_or("").trim().to_lowercase();
    let pattern = format!("%{q}%");
    let lim = limit.unwrap_or(200).clamp(1, MAX_RESULTS) as i64;

    let mut stmt = conn
        .prepare(
            "SELECT id, ts, action, source, result, message, target_language
             FROM history
             WHERE (?1 = '' OR lower(source) LIKE ?2 OR lower(result) LIKE ?2)
               AND (?3 = '' OR action = ?3)
             ORDER BY ts DESC
             LIMIT ?4",
        )
        .map_err(|e| format!("prepare history search: {e}"))?;

    let rows = stmt
        .query_map(params![q, pattern, action, lim], |row| {
            Ok(HistoryEntry {
                id: row.get(0)?,
                ts: row.get::<_, i64>(1)? as u64,
                action: row.get(2)?,
                source: row.get(3)?,
                result: row.get(4)?,
                message: row.get(5)?,
                target_language: row.get(6)?,
            })
        })
        .map_err(|e| format!("search history: {e}"))?;

    rows.collect::<Result<Vec<_>, _>>()
        .map_err(|e| format!("read history: {e}"))
}

pub fn list(limit: Option<usize>) -> Result<Vec<HistoryEntry>, String> {
    search(None, None, limit)
}

pub fn delete(id: &str) -> Result<(), String> {
    let conn = open_db()?;
    conn.execute("DELETE FROM history WHERE id = ?1", params![id])
        .map_err(|e| format!("delete history entry: {e}"))?;
    Ok(())
}

pub fn clear() -> Result<(), String> {
    let conn = open_db()?;
    conn.execute("DELETE FROM history", [])
        .map_err(|e| format!("clear history: {e}"))?;
    Ok(())
}

pub fn apply_retention() -> Result<(), String> {
    let conn = open_db()?;
    cleanup_retention(&conn, config::load().history_retention_days)
}

fn truncate(s: &str, max_chars: usize) -> String {
    let count = s.chars().count();
    if count <= max_chars {
        return s.to_owned();
    }
    s.chars().take(max_chars).collect::<String>() + "…"
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn truncate_keeps_short_text() {
        assert_eq!(truncate("hello", 10), "hello");
    }

    #[test]
    fn truncate_limits_long_text() {
        assert_eq!(truncate("abcdef", 3), "abc…");
    }
}
