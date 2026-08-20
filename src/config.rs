use anyhow::Result;
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::Path;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Config {
    pub model: ModelConfig,
    pub hotkey: HotkeyConfig,
    pub prompt: PromptConfig,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ModelConfig {
    pub active_model: String,
    pub hf_repo: String,
    pub hf_filename: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HotkeyConfig {
    pub double_ctrl_timeout_ms: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PromptConfig {
    pub system_prompt: String,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            model: ModelConfig {
                active_model: "models/qwen2.5-0.5b-instruct-q4_k_m.gguf".to_string(),
                hf_repo: "Qwen/Qwen2.5-0.5B-Instruct-GGUF".to_string(),
                hf_filename: "qwen2.5-0.5b-instruct-q4_k_m.gguf".to_string(),
            },
            hotkey: HotkeyConfig {
                double_ctrl_timeout_ms: 350,
            },
            prompt: PromptConfig {
                system_prompt: "You are a professional English grammar corrector. Correct spelling, grammar, and punctuation in the text provided. Output ONLY the revised English text. Do NOT add any explanations, introductory text, quotes, or markdown code blocks.".to_string(),
            },
        }
    }
}

impl Config {
    pub fn load_or_create(path: &Path) -> Result<Self> {
        if path.exists() {
            let content = fs::read_to_string(path)?;
            let config: Config = toml::from_str(&content)?;
            Ok(config)
        } else {
            let config = Config::default();
            let content = toml::to_string_pretty(&config)?;
            fs::write(path, content)?;
            Ok(config)
        }
    }
}
