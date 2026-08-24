//! Butchi adapter for llama.cpp through rs-llama.

use std::{
    fs,
    path::{Path, PathBuf},
};

use once_cell::sync::Lazy;
use parking_lot::Mutex;
use rs_llama::{EngineConfig as RsEngineConfig, GenerateRequest, LlamaEngine};

use crate::{
    config::{self, AppConfig},
    core_logic,
};

static ENGINE: Lazy<Mutex<Option<LoadedModel>>> = Lazy::new(|| Mutex::new(None));

/// Minimum llama.cpp context size (prompt + generation headroom).
const MIN_CONTEXT_SIZE: u32 = 10_000;

struct LoadedModel {
    path: PathBuf,
    engine: LlamaEngine,
    gpu_layers: u32,
    ctx_size: u32,
}

fn build_prompt(system: &str, user: &str) -> String {
    format!(
        "<|im_start|>system\n{system}<|im_end|>\n<|im_start|>user\n{user}<|im_end|>\n<|im_start|>assistant\n"
    )
}

fn context_size(cfg: &AppConfig) -> u32 {
    // Always at least 10K; grow further if max_tokens needs more headroom.
    cfg.max_tokens.saturating_add(2048).max(MIN_CONTEXT_SIZE)
}

fn load_engine(path: &Path, gpu_layers: u32, ctx_size: u32) -> Result<LlamaEngine, String> {
    LlamaEngine::load(
        RsEngineConfig::new(path)
            .with_ctx_size(ctx_size)
            .with_gpu_layers(gpu_layers),
    )
    .map_err(|error| format!("load GGUF model {}: {error}", path.display()))
}

fn load_model(path: &Path, gpu_layers: u32, ctx_size: u32) -> Result<(), String> {
    let mut guard = ENGINE.lock();
    if guard.as_ref().is_some_and(|loaded| {
        loaded.path == path && loaded.gpu_layers == gpu_layers && loaded.ctx_size == ctx_size
    }) {
        return Ok(());
    }

    *guard = None;
    let engine = load_engine(path, gpu_layers, ctx_size)?;
    *guard = Some(LoadedModel {
        path: path.to_owned(),
        engine,
        gpu_layers,
        ctx_size,
    });
    Ok(())
}

fn generate_loaded<F>(
    system: &str,
    user: &str,
    cfg: &AppConfig,
    mut on_piece: F,
) -> Result<String, String>
where
    F: FnMut(&str),
{
    let guard = ENGINE.lock();
    let loaded = guard
        .as_ref()
        .ok_or_else(|| "model is not loaded".to_string())?;

    let max_tokens = i32::try_from(cfg.max_tokens)
        .map_err(|_| "max tokens is too large for the inference backend".to_string())?;
    let mut request = GenerateRequest::new(build_prompt(system, user)).with_max_tokens(max_tokens);
    request.temperature = cfg.temperature.max(0.0);
    request.seed = 42;

    loaded
        .engine
        .generate_with_callback(&request, |piece| on_piece(piece))
        .map(|output| output.trim().to_owned())
        .map_err(|error| format!("generate response: {error}"))
}

fn compiled_gpu_backend() -> Option<&'static str> {
    #[cfg(feature = "cuda")]
    {
        return Some("cuda");
    }
    #[cfg(all(feature = "vulkan", not(feature = "cuda")))]
    {
        return Some("vulkan");
    }
    #[cfg(not(any(feature = "cuda", feature = "vulkan")))]
    {
        None
    }
}

fn preferred_gpu_layers(cfg: &AppConfig) -> Result<u32, String> {
    core_logic::preferred_gpu_layers(
        &cfg.backend_preference,
        cfg.gpu_layers,
        compiled_gpu_backend().is_some(),
    )
}

pub fn model_status(cfg: &AppConfig) -> ModelStatus {
    let path = config::model_local_path(&cfg.model_repo, &cfg.model_file).ok();
    let downloaded = path.as_ref().is_some_and(|candidate| candidate.is_file());
    let guard = ENGINE.lock();
    let loaded_model = guard
        .as_ref()
        .filter(|loaded| path.as_ref() == Some(&loaded.path));
    let compiled = compiled_gpu_backend().unwrap_or("cpu");
    let backend = loaded_model
        .map(|loaded| if loaded.gpu_layers > 0 { compiled } else { "cpu" })
        .unwrap_or_else(|| match config::normalize_backend_preference(&cfg.backend_preference) {
            "cpu" => "cpu",
            "gpu" | "auto" if compiled_gpu_backend().is_some() => compiled,
            _ => "cpu",
        });

    ModelStatus {
        downloaded,
        loaded: loaded_model.is_some(),
        local_path: path.and_then(|candidate| candidate.to_str().map(str::to_owned)),
        repo: cfg.model_repo.clone(),
        file: cfg.model_file.clone(),
        gpu_feature: compiled.into(),
        backend: backend.into(),
        devices: detect_devices(),
        gpu_offload_available: compiled_gpu_backend().is_some(),
        max_devices: 1,
    }
}

pub fn unload() {
    *ENGINE.lock() = None;
}

pub fn download_model(repo: &str, file: &str) -> Result<PathBuf, String> {
    let dest = config::model_local_path(repo, file)?;
    if dest.is_file() {
        return Ok(dest);
    }

    config::ensure_parent(&dest)?;
    let api = hf_hub::api::sync::Api::new()
        .map_err(|error| format!("create Hugging Face client: {error}"))?;
    let cached = api
        .model(repo.to_owned())
        .get(file)
        .map_err(|error| format!("download {repo}/{file}: {error}"))?;
    let temp = dest.with_extension("gguf.download");
    fs::copy(&cached, &temp).map_err(|error| format!("copy downloaded model: {error}"))?;
    fs::rename(&temp, &dest).map_err(|error| format!("finish model download: {error}"))?;
    Ok(dest)
}

pub fn ensure_loaded(cfg: &AppConfig) -> Result<(), String> {
    let path = config::model_local_path(&cfg.model_repo, &cfg.model_file)?;
    if !path.is_file() {
        return Err(format!(
            "model is not downloaded: {}/{}",
            cfg.model_repo, cfg.model_file
        ));
    }

    let desired_gpu_layers = preferred_gpu_layers(cfg)?;
    let ctx_size = context_size(cfg);
    match load_model(&path, desired_gpu_layers, ctx_size) {
        Ok(()) => Ok(()),
        Err(gpu_error)
            if desired_gpu_layers > 0
                && config::normalize_backend_preference(&cfg.backend_preference) == "auto" =>
        {
            load_model(&path, 0, ctx_size).map_err(|cpu_error| {
                format!(
                    "GPU load failed ({gpu_error}); CPU fallback also failed ({cpu_error})"
                )
            })
        }
        Err(error) => Err(error),
    }
}

#[allow(dead_code)]
pub fn generate(system: &str, user: &str, cfg: &AppConfig) -> Result<String, String> {
    ensure_loaded(cfg)?;
    generate_loaded(system, user, cfg, |_| {})
}

pub fn generate_streaming<F>(
    system: &str,
    user: &str,
    cfg: &AppConfig,
    on_piece: F,
) -> Result<String, String>
where
    F: FnMut(&str),
{
    ensure_loaded(cfg)?;
    generate_loaded(system, user, cfg, on_piece)
}

fn detect_devices() -> Vec<BackendDevice> {
    let mut devices = vec![BackendDevice {
        id: "cpu".into(),
        name: "CPU".into(),
        backend: "cpu".into(),
        description: "CPU inference through rs-llama / llama.cpp".into(),
    }];

    if let Some(backend) = compiled_gpu_backend() {
        devices.insert(
            0,
            BackendDevice {
                id: backend.into(),
                name: if backend == "cuda" {
                    "NVIDIA CUDA"
                } else {
                    "Vulkan GPU"
                }
                .into(),
                backend: backend.into(),
                description: format!(
                    "{backend} backend compiled in; Auto falls back to CPU if GPU model loading fails"
                ),
            },
        );
    }
    devices
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn backend_policy_matches_compiled_features() {
        let cfg = AppConfig {
            backend_preference: "cpu".into(),
            ..AppConfig::default()
        };
        assert_eq!(preferred_gpu_layers(&cfg).unwrap(), 0);

        let cfg = AppConfig {
            backend_preference: "auto".into(),
            ..AppConfig::default()
        };
        assert_eq!(
            preferred_gpu_layers(&cfg).unwrap() > 0,
            compiled_gpu_backend().is_some()
        );

        let cfg = AppConfig {
            backend_preference: "gpu".into(),
            ..AppConfig::default()
        };
        assert_eq!(
            preferred_gpu_layers(&cfg).is_ok(),
            compiled_gpu_backend().is_some()
        );
    }

    #[test]
    fn custom_system_and_user_prompts_are_preserved() {
        let prompt = build_prompt("translate exactly", "Hello");
        assert!(prompt.contains("<|im_start|>system\ntranslate exactly<|im_end|>"));
        assert!(prompt.contains("<|im_start|>user\nHello<|im_end|>"));
        assert!(prompt.ends_with("<|im_start|>assistant\n"));
    }

    #[test]
    fn context_size_is_at_least_10k() {
        let cfg = AppConfig {
            max_tokens: 256,
            ..AppConfig::default()
        };
        assert_eq!(context_size(&cfg), 10_000);

        let cfg = AppConfig {
            max_tokens: 9000,
            ..AppConfig::default()
        };
        assert_eq!(context_size(&cfg), 11_048);
    }

    #[test]
    fn missing_model_is_rejected_without_inference() {
        let cfg = AppConfig {
            model_repo: "butchi-test/nonexistent".into(),
            model_file: "definitely-missing.gguf".into(),
            ..AppConfig::default()
        };
        assert!(
            ensure_loaded(&cfg)
                .unwrap_err()
                .contains("model is not downloaded")
        );
    }

    #[test]
    fn device_list_always_contains_cpu() {
        assert!(detect_devices().iter().any(|device| device.backend == "cpu"));
    }
}
