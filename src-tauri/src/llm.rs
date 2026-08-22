//! LLM / GGUF backend. Enabled by feature `llm` (default).

#[cfg(feature = "llm")]
#[path = "llm_engine.rs"]
mod engine;

#[cfg(feature = "llm")]
pub use engine::*;

#[cfg(not(feature = "llm"))]
mod stubs {
    use std::path::PathBuf;

    use crate::config::{self, AppConfig};

    #[derive(serde::Serialize, Clone)]
    #[serde(rename_all = "camelCase")]
    pub struct BackendDevice {
        pub id: String,
        pub name: String,
        pub backend: String,
        pub description: String,
    }

    #[derive(serde::Serialize, Clone)]
    #[serde(rename_all = "camelCase")]
    pub struct ModelStatus {
        pub downloaded: bool,
        pub loaded: bool,
        pub local_path: Option<String>,
        pub repo: String,
        pub file: String,
        pub gpu_feature: String,
        pub backend: String,
        pub devices: Vec<BackendDevice>,
        pub gpu_offload_available: bool,
        pub max_devices: u32,
    }

    pub fn model_status(config: &AppConfig) -> ModelStatus {
        let path = config::model_local_path(&config.model_repo, &config.model_file).ok();
        let downloaded = path.as_ref().is_some_and(|p| p.is_file());
        ModelStatus {
            downloaded,
            loaded: false,
            local_path: path.and_then(|p| p.to_str().map(str::to_owned)),
            repo: config.model_repo.clone(),
            file: config.model_file.clone(),
            gpu_feature: "cpu".into(),
            backend: "cpu".into(),
            devices: vec![BackendDevice {
                id: "cpu".into(),
                name: "CPU".into(),
                backend: "cpu".into(),
                description: "LLM disabled (build with default features)".into(),
            }],
            gpu_offload_available: false,
            max_devices: 1,
        }
    }

    pub fn download_model(_repo: &str, _file: &str) -> Result<PathBuf, String> {
        Err("LLM not compiled in (use default features)".into())
    }

    pub fn unload() {}

    pub fn ensure_loaded(_config: &AppConfig) -> Result<(), String> {
        Err("LLM not compiled in (use default features)".into())
    }

    pub fn generate(_system: &str, _user: &str, _config: &AppConfig) -> Result<String, String> {
        Err("LLM not compiled in (use default features)".into())
    }

    pub fn generate_streaming<F>(
        _system: &str,
        _user: &str,
        _config: &AppConfig,
        _on_piece: F,
    ) -> Result<String, String>
    where
        F: FnMut(&str),
    {
        Err("LLM not compiled in (use default features)".into())
    }
}

#[cfg(not(feature = "llm"))]
pub use stubs::*;
