use std::fs;
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};

use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};

use crate::actions::TextAction;
use crate::{config, core_logic};

const DB_FILE: &str = "history.db";
const LEGACY_HISTORY_FILE: &str = "history.json";
const MAX_RESULTS: usize = 500;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HistoryEntry { pub id: String, pub ts: u64, pub action: String, pub source: String, pub result: String, pub message: String, #[serde(default)] pub target_language: Option<String> }

fn db_path() -> Result<PathBuf, String> { Ok(config::app_data_dir()?.join(DB_FILE)) }
fn legacy_path() -> Result<PathBuf, String> { Ok(config::app_data_dir()?.join(LEGACY_HISTORY_FILE)) }
fn now_ms() -> u64 { SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_millis() as u64).unwrap_or(0) }

fn initialize_db(conn: &Connection) -> Result<(), String> {
    conn.execute_batch("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS history (id TEXT PRIMARY KEY, ts INTEGER NOT NULL, action TEXT NOT NULL, source TEXT NOT NULL, result TEXT NOT NULL, message TEXT NOT NULL, target_language TEXT NULL); CREATE INDEX IF NOT EXISTS idx_history_ts ON history(ts DESC); CREATE INDEX IF NOT EXISTS idx_history_action ON history(action);").map_err(|e| format!("initialize history database: {e}"))
}
fn open_db() -> Result<Connection, String> { let conn = Connection::open(db_path()?).map_err(|e| format!("open history database: {e}"))?; initialize_db(&conn)?; migrate_legacy_json(&conn)?; Ok(conn) }
fn migrate_entries(conn: &Connection, entries: Vec<HistoryEntry>) -> Result<(), String> { for e in entries { conn.execute("INSERT OR IGNORE INTO history (id, ts, action, source, result, message, target_language) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)", params![e.id, e.ts as i64, e.action, e.source, e.result, e.message, e.target_language]).map_err(|err| format!("migrate history row: {err}"))?; } Ok(()) }
fn migrate_legacy_json(conn: &Connection) -> Result<(), String> { let path = legacy_path()?; if !path.is_file() { return Ok(()); } let raw = fs::read_to_string(&path).map_err(|e| format!("read legacy history: {e}"))?; let entries: Vec<HistoryEntry> = serde_json::from_str(&raw).unwrap_or_default(); migrate_entries(conn, entries)?; let _ = fs::rename(&path, path.with_extension("migrated.json")); Ok(()) }
fn cleanup_retention_at(conn: &Connection, retention_days: i32, now: u64) -> Result<(), String> { if retention_days < 0 { return Ok(()); } if retention_days == 0 { conn.execute("DELETE FROM history", []).map_err(|e| format!("clear disabled history: {e}"))?; return Ok(()); } let cutoff = now.saturating_sub(retention_days as u64 * 86_400_000); conn.execute("DELETE FROM history WHERE ts < ?1", params![cutoff as i64]).map_err(|e| format!("prune history: {e}"))?; Ok(()) }
fn cleanup_retention(conn: &Connection, retention_days: i32) -> Result<(), String> { cleanup_retention_at(conn, retention_days, now_ms()) }
fn search_conn(conn: &Connection, query: Option<&str>, action: Option<&str>, limit: Option<usize>) -> Result<Vec<HistoryEntry>, String> { let q = query.unwrap_or("").trim().to_lowercase(); let action = action.unwrap_or("").trim().to_lowercase(); let pattern = format!("%{q}%"); let lim = limit.unwrap_or(200).clamp(1, MAX_RESULTS) as i64; let mut stmt = conn.prepare("SELECT id, ts, action, source, result, message, target_language FROM history WHERE (?1 = '' OR lower(source) LIKE ?2 OR lower(result) LIKE ?2) AND (?3 = '' OR action = ?3) ORDER BY ts DESC LIMIT ?4").map_err(|e| format!("prepare history search: {e}"))?; let rows = stmt.query_map(params![q, pattern, action, lim], |row| Ok(HistoryEntry { id: row.get(0)?, ts: row.get::<_, i64>(1)? as u64, action: row.get(2)?, source: row.get(3)?, result: row.get(4)?, message: row.get(5)?, target_language: row.get(6)? })).map_err(|e| format!("search history: {e}"))?; rows.collect::<Result<Vec<_>, _>>().map_err(|e| format!("read history: {e}")) }

pub fn append(action: TextAction, source: &str, result: &str, message: &str, target_language: Option<&str>) { let cfg = config::load(); if cfg.history_retention_days == 0 { return; } let action_name = match action { TextAction::Translate => "translate", TextAction::Rewrite => "rewrite" }; let ts = now_ms(); let entry = HistoryEntry { id: format!("{ts}-{action_name}"), ts, action: action_name.into(), source: core_logic::truncate_text(source, 8000), result: core_logic::truncate_text(result, 16000), message: core_logic::truncate_text(message, 1000), target_language: target_language.map(|s| core_logic::truncate_text(s, 100)) }; if let Ok(conn) = open_db() { let _ = cleanup_retention(&conn, cfg.history_retention_days); let _ = conn.execute("INSERT OR REPLACE INTO history (id, ts, action, source, result, message, target_language) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)", params![entry.id, entry.ts as i64, entry.action, entry.source, entry.result, entry.message, entry.target_language]); } }
pub fn search(query: Option<&str>, action: Option<&str>, limit: Option<usize>) -> Result<Vec<HistoryEntry>, String> { let conn = open_db()?; cleanup_retention(&conn, config::load().history_retention_days)?; search_conn(&conn, query, action, limit) }
pub fn list(limit: Option<usize>) -> Result<Vec<HistoryEntry>, String> { search(None, None, limit) }
pub fn delete(id: &str) -> Result<(), String> { let conn = open_db()?; conn.execute("DELETE FROM history WHERE id = ?1", params![id]).map_err(|e| format!("delete history entry: {e}"))?; Ok(()) }
pub fn clear() -> Result<(), String> { let conn = open_db()?; conn.execute("DELETE FROM history", []).map_err(|e| format!("clear history: {e}"))?; Ok(()) }
pub fn apply_retention() -> Result<(), String> { let conn = open_db()?; cleanup_retention(&conn, config::load().history_retention_days) }

#[cfg(test)]
mod tests {
    use super::*;
    fn entry(id: &str, ts: u64, action: &str, source: &str, result: &str) -> HistoryEntry { HistoryEntry { id: id.into(), ts, action: action.into(), source: source.into(), result: result.into(), message: "ok".into(), target_language: None } }
    fn memory_db() -> Connection { let conn = Connection::open_in_memory().unwrap(); initialize_db(&conn).unwrap(); conn }
    fn insert(conn: &Connection, e: &HistoryEntry) { migrate_entries(conn, vec![e.clone()]).unwrap(); }
    #[test] fn sqlite_search_filters_orders_and_limits() { let conn = memory_db(); insert(&conn, &entry("1", 10, "rewrite", "Hello World", "Fixed text")); insert(&conn, &entry("2", 30, "translate", "Good morning", "Xin chao")); insert(&conn, &entry("3", 20, "translate", "HELLO again", "Xin chao lan nua")); let all = search_conn(&conn, None, None, None).unwrap(); assert_eq!(all.iter().map(|e| e.id.as_str()).collect::<Vec<_>>(), vec!["2", "3", "1"]); assert_eq!(search_conn(&conn, Some("hello"), None, None).unwrap().len(), 2); assert_eq!(search_conn(&conn, Some("xin CHAO"), Some(" TRANSLATE "), None).unwrap().len(), 2); assert_eq!(search_conn(&conn, None, None, Some(1)).unwrap().len(), 1); assert_eq!(search_conn(&conn, None, None, Some(0)).unwrap().len(), 1); assert_eq!(search_conn(&conn, None, None, Some(9999)).unwrap().len(), 3); }
    #[test] fn retention_covers_forever_disabled_day_cutoff_and_saturating_cutoff() { let conn = memory_db(); insert(&conn, &entry("old", 1_000, "rewrite", "a", "b")); insert(&conn, &entry("new", 200_000_000, "rewrite", "a", "b")); cleanup_retention_at(&conn, -1, 200_000_000).unwrap(); assert_eq!(search_conn(&conn, None, None, None).unwrap().len(), 2); cleanup_retention_at(&conn, 1, 200_000_000).unwrap(); assert_eq!(search_conn(&conn, None, None, None).unwrap()[0].id, "new"); cleanup_retention_at(&conn, 30, 1).unwrap(); assert_eq!(search_conn(&conn, None, None, None).unwrap().len(), 1); cleanup_retention_at(&conn, 0, 200_000_000).unwrap(); assert!(search_conn(&conn, None, None, None).unwrap().is_empty()); }
    #[test] fn migration_ignores_duplicates_and_preserves_language() { let conn = memory_db(); let mut first = entry("same", 1, "translate", "hello", "xin chao"); first.target_language = Some("Vietnamese".into()); migrate_entries(&conn, vec![first, entry("same", 2, "rewrite", "other", "other")]).unwrap(); let rows = search_conn(&conn, None, None, None).unwrap(); assert_eq!(rows.len(), 1); assert_eq!(rows[0].target_language.as_deref(), Some("Vietnamese")); }
}
