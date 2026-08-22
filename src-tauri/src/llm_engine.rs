use std::{
    fs,
    num::NonZeroU32,
    path::{Path, PathBuf},
};

use llama_cpp_2::{
    context::params::LlamaContextParams,
    llama_backend::LlamaBackend,
    llama_batch::LlamaBatch,
    model::{params::LlamaModelParams, AddBos, LlamaChatMessage, LlamaModel},
    sampling::LlamaSampler,
};
use once_cell::sync::Lazy;
use parking_lot::Mutex;

use crate::config::{self, AppConfig};

static ENGINE: Lazy<Mutex<Option<LoadedModel>>> = Lazy::new(|| Mutex::new(None));
static BACKEND: Lazy<Mutex<Option<LlamaBackend>>> = Lazy::new(|| Mutex::new(None));

struct LoadedModel {
    path: PathBuf,
    model: LlamaModel,
}

fn ensure_backend() -> Result<(), String> {
    let mut guard = BACKEND.lock();
    if guard.is_none() {
        let backend = LlamaBackend::init().map_err(|e| format!("llama backend init: {e}"))?;
        *guard = Some(backend);
    }
    Ok(())
}

pub fn model_status(config: &AppConfig) -> ModelStatus {
    let path = config::model_local_path(&config.model_repo, &config.model_file).ok();
    let downloaded = path.as_ref().is_some_and(|p| p.is_file());
    let loaded = ENGINE
        .lock()
        .as_ref()
        .map(|m| m.path.clone())
        .filter(|p| path.as_ref() == Some(p))
        .is_some();

    let backend = current_backend();
    let devices = detect_devices();

    ModelStatus {
        downloaded,
        loaded,
        local_path: path.and_then(|p| p.to_str().map(str::to_owned)),
        repo: config.model_repo.clone(),
        file: config.model_file.clone(),
        gpu_feature: backend.clone(),
        backend,
        devices,
        gpu_offload_available: cfg!(any(feature = "cuda", feature = "vulkan")),
        max_devices: llama_cpp_2::max_devices() as u32,
    }
}

fn current_backend() -> String {
    #[cfg(feature = "cuda")]
    {
        return "cuda".into();
    }
    #[cfg(all(feature = "vulkan", not(feature = "cuda")))]
    {
        return "vulkan".into();
    }
    #[cfg(not(any(feature = "cuda", feature = "vulkan")))]
    {
        "cpu".into()
    }
}

pub fn unload() {
    *ENGINE.lock() = None;
}

pub fn download_model(repo: &str, file: &str) -> Result<PathBuf, String> {
    Err("placeholder - full engine in local copy".into())
}

pub fn ensure_loaded(_config: &AppConfig) -> Result<(), String> {
    Err("placeholder".into())
}

pub fn generate(_system: &str, _user: &str, _config: &AppConfig) -> Result<String, String> {
    Err("placeholder".into())
}

fn detect_devices() -> Vec<BackendDevice> {
    vec![BackendDevice {
        id: "cpu".into(),
        name: "CPU".into(),
        backend: "cpu".into(),
        description: "CPU inference".into(),
    }]
}

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
