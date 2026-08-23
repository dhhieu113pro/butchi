use std::{
    fs,
    path::{Path, PathBuf},
};

use serde::{Deserialize, Serialize};

use crate::core_logic;

const CONFIG_FILE: &str = "config.json";

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct AppConfig {
    pub translate_enabled: bool,
    pub rewrite_enabled: bool,
    pub target_language: String,
    pub favorite_languages: Vec<String>,
    pub rewrite_system_prompt: String,
    pub translate_system_prompt: String,
    pub result_action: String,
    pub backend_preference: String,
    pub model_repo: String,
    pub model_file: String,
    pub max_tokens: u32,
    pub temperature: f32,
    pub gpu_layers: u32,
    pub history_retention_days: i32,
}

impl Default for AppConfig {
    fn default() -> Self {
        Self {
            translate_enabled: true,
            rewrite_enabled: true,
            target_language: "Vietnamese".into(),
            favorite_languages: vec!["Vietnamese".into(), "English".into()],
            rewrite_system_prompt: "You are a precise writing assistant. Rewrite the user's text so it is clear, natural, and grammatically correct. Keep the original meaning and language. Output only the rewritten text with no quotes or explanation.".into(),
            translate_system_prompt: "You are a precise translation assistant. Translate the user's text into the target language. Keep meaning and tone. Output only the translation with no quotes or explanation.".into(),
            result_action: "copy".into(),
            backend_preference: "auto".into(),
            model_repo: "unsloth/Qwen3.5-0.8B-GGUF".into(),
            model_file: "Qwen3.5-0.8B-Q4_K_M.gguf".into(),
            max_tokens: 256,
            temperature: 0.3,
            gpu_layers: 999,
            history_retention_days: 30,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ModelOption {
    pub id: String,
    pub label: String,
    pub repo: String,
    pub file: String,
    pub size_hint: String,
}

pub fn model_catalog() -> Vec<ModelOption> {
    vec![
        ModelOption { id: "qwen35-0.8b-q4".into(), label: "Qwen3.5 0.8B (Q4_K_M) — default".into(), repo: "unsloth/Qwen3.5-0.8B-GGUF".into(), file: "Qwen3.5-0.8B-Q4_K_M.gguf".into(), size_hint: "~530 MB".into() },
        ModelOption { id: "qwen35-0.8b-q5".into(), label: "Qwen3.5 0.8B (Q5_K_M)".into(), repo: "unsloth/Qwen3.5-0.8B-GGUF".into(), file: "Qwen3.5-0.8B-Q5_K_M.gguf".into(), size_hint: "~590 MB".into() },
        ModelOption { id: "qwen3-0.6b-q4".into(), label: "Qwen3 0.6B (Q4_K_M)".into(), repo: "unsloth/Qwen3-0.6B-GGUF".into(), file: "Qwen3-0.6B-Q4_K_M.gguf".into(), size_hint: "~400 MB".into() },
    ]
}

pub fn app_data_dir() -> Result<PathBuf, String> {
    let base = dirs::data_dir().ok_or_else(|| "could not resolve app data directory".to_string())?;
    let dir = base.join("butchi");
    fs::create_dir_all(&dir).map_err(|e| format!("create data dir: {e}"))?;
    Ok(dir)
}

pub fn models_dir() -> Result<PathBuf, String> {
    let dir = app_data_dir()?.join("models");
    fs::create_dir_all(&dir).map_err(|e| format!("create models dir: {e}"))?;
    Ok(dir)
}

fn config_path() -> Result<PathBuf, String> { Ok(app_data_dir()?.join(CONFIG_FILE)) }

pub fn load() -> AppConfig {
    let Ok(path) = config_path() else { return AppConfig::default(); };
    match fs::read_to_string(&path) {
        Ok(raw) => serde_json::from_str(&raw).unwrap_or_default(),
        Err(_) => AppConfig::default(),
    }
}

pub fn save(config: &AppConfig) -> Result<(), String> {
    let path = config_path()?;
    let raw = serde_json::to_string_pretty(config).map_err(|e| e.to_string())?;
    fs::write(&path, raw).map_err(|e| format!("write config: {e}"))
}

pub fn update_target_language(config: &mut AppConfig, language: &str) -> Result<(), String> {
    config.target_language = core_logic::normalize_target_language(language)?;
    Ok(())
}

pub fn normalize_backend_preference(value: &str) -> &'static str {
    core_logic::normalize_backend_preference(value)
}

pub fn model_local_path(repo: &str, file: &str) -> Result<PathBuf, String> {
    let safe_repo = repo.replace('/', "__");
    Ok(models_dir()?.join(safe_repo).join(file))
}

pub fn model_is_downloaded(repo: &str, file: &str) -> bool {
    model_local_path(repo, file).map(|p| p.is_file()).unwrap_or(false)
}

pub fn ensure_parent(path: &Path) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|e| format!("create model parent: {e}"))?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_and_old_config_migration_are_stable() {
        let defaults = AppConfig::default();
        assert_eq!(defaults.result_action, "copy");
        assert_eq!(defaults.backend_preference, "auto");
        assert!(defaults.translate_system_prompt.starts_with("You are a precise translation assistant."));
        assert!(defaults.rewrite_system_prompt.starts_with("You are a precise writing assistant."));

        let raw = r#"{"translateEnabled":true,"rewriteEnabled":true,"targetLanguage":"English"}"#;
        let config: AppConfig = serde_json::from_str(raw).unwrap();
        assert_eq!(config.target_language, "English");
        assert_eq!(config.favorite_languages, vec!["Vietnamese", "English"]);
        assert_eq!(config.result_action, "copy");
        assert_eq!(config.backend_preference, "auto");
    }

    #[test]
    fn configurable_values_round_trip() {
        let mut config = AppConfig::default();
        config.result_action = "replace".into();
        config.favorite_languages = vec!["English".into(), "Japanese".into(), "German".into()];
        config.target_language = "Japanese".into();
        config.translate_system_prompt = "Translate like a technical localization expert.".into();
        config.rewrite_system_prompt = "Rewrite as concise release notes.".into();
        let json = serde_json::to_string(&config).unwrap();
        let restored: AppConfig = serde_json::from_str(&json).unwrap();
        assert_eq!(restored.result_action, "replace");
        assert_eq!(restored.favorite_languages, config.favorite_languages);
        assert_eq!(restored.target_language, "Japanese");
        assert_eq!(restored.translate_system_prompt, config.translate_system_prompt);
        assert_eq!(restored.rewrite_system_prompt, config.rewrite_system_prompt);
    }

    #[test]
    fn target_and_backend_helpers_delegate_to_core_logic() {
        let mut config = AppConfig::default();
        update_target_language(&mut config, "  Japanese  ").unwrap();
        assert_eq!(config.target_language, "Japanese");
        assert!(update_target_language(&mut config, "   ").is_err());
        assert_eq!(normalize_backend_preference("GPU"), "gpu");
        assert_eq!(normalize_backend_preference("other"), "auto");
    }

    #[test]
    fn model_catalog_has_expected_defaults() {
        let models = model_catalog();
        assert_eq!(models.len(), 3);
        assert_eq!(models[0].id, "qwen35-0.8b-q4");
        assert!(models.iter().all(|m| !m.repo.is_empty() && !m.file.is_empty()));
    }
}
