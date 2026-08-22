//! Butchi adapter for the inference approach used by
//! https://github.com/dhhieu113pro/llama-rust (commit 360d3d6).

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

static ENGINE: Lazy<Mutex<Option<LlamaEngine>>> = Lazy::new(|| Mutex::new(None));

struct LlamaEngine {
    backend: LlamaBackend,
    loaded: Option<LoadedModel>,
}

struct LoadedModel {
    path: PathBuf,
    model: LlamaModel,
}

impl LlamaEngine {
    fn new() -> Result<Self, String> {
        let backend = LlamaBackend::init().map_err(|e| format!("llama backend init: {e}"))?;
        Ok(Self {
            backend,
            loaded: None,
        })
    }

    fn load(&mut self, path: &Path, gpu_layers: u32) -> Result<(), String> {
        if self.loaded.as_ref().is_some_and(|loaded| loaded.path == path) {
            return Ok(());
        }

        let params = make_model_params(gpu_layers);
        let model = LlamaModel::load_from_file(&self.backend, path, &params)
            .map_err(|e| format!("load GGUF model {}: {e}", path.display()))?;
        self.loaded = Some(LoadedModel {
            path: path.to_owned(),
            model,
        });
        Ok(())
    }

    fn generate(&mut self, system: &str, user: &str, config: &AppConfig) -> Result<String, String> {
        self.generate_streaming(system, user, config, |_| {})
    }

    fn generate_streaming<F>(
        &mut self,
        system: &str,
        user: &str,
        config: &AppConfig,
        mut on_piece: F,
    ) -> Result<String, String>
    where
        F: FnMut(&str),
    {
        let loaded = self
            .loaded
            .as_ref()
            .ok_or_else(|| "model is not loaded".to_string())?;
        let prompt = chat_prompt(&loaded.model, system, user)?;
        let prompt_tokens = loaded
            .model
            .str_to_token(&prompt, AddBos::Always)
            .map_err(|e| format!("tokenize prompt: {e}"))?;
        if prompt_tokens.is_empty() {
            return Err("prompt produced no tokens".into());
        }

        let max_tokens = config.max_tokens as usize;
        let required_context = prompt_tokens
            .len()
            .checked_add(max_tokens)
            .ok_or_else(|| "requested context is too large".to_string())?;
        let training_context = loaded.model.n_ctx_train() as usize;
        if required_context > training_context {
            return Err(format!(
                "prompt and output need {required_context} tokens, but the model supports {training_context}"
            ));
        }
        let context_size = NonZeroU32::new(required_context.max(512) as u32)
            .ok_or_else(|| "context size must be greater than zero".to_string())?;
        let context_params = LlamaContextParams::default().with_n_ctx(Some(context_size));
        let mut context = loaded
            .model
            .new_context(&self.backend, context_params)
            .map_err(|e| format!("create llama context: {e}"))?;

        let mut batch = LlamaBatch::new(prompt_tokens.len().max(1), 1);
        let last_prompt_index = prompt_tokens.len() - 1;
        for (index, token) in prompt_tokens.iter().copied().enumerate() {
            batch.add(token, index as i32, &[0], index == last_prompt_index)
                .map_err(|e| format!("prepare prompt batch: {e}"))?;
        }
        context
            .decode(&mut batch)
            .map_err(|e| format!("decode prompt: {e}"))?;

        let temperature = config.temperature.max(0.0);
        let mut sampler = if temperature == 0.0 {
            LlamaSampler::greedy()
        } else {
            LlamaSampler::chain_simple([
                LlamaSampler::temp(temperature),
                LlamaSampler::dist(42),
            ])
        };
        let mut decoder = encoding_rs::UTF_8.new_decoder();
        let mut output = String::new();
        let mut position = prompt_tokens.len() as i32;

        for _ in 0..max_tokens {
            let token = sampler.sample(&context, batch.n_tokens() - 1);
            sampler.accept(token);
            if loaded.model.is_eog_token(token) {
                break;
            }
            let piece = loaded
                .model
                .token_to_piece(token, &mut decoder, true, None)
                .map_err(|e| format!("decode output token: {e}"))?;
            output.push_str(&piece);
            if !piece.is_empty() {
                on_piece(&piece);
            }
            batch.clear();
            batch.add(token, position, &[0], true)
                .map_err(|e| format!("prepare output batch: {e}"))?;
            context
                .decode(&mut batch)
                .map_err(|e| format!("decode output token: {e}"))?;
            position += 1;
        }

        Ok(output.trim().to_owned())
    }
}

fn chat_prompt(model: &LlamaModel, system: &str, user: &str) -> Result<String, String> {
    let messages = [
        LlamaChatMessage::new("system".into(), system.into())
            .map_err(|e| format!("invalid system prompt: {e}"))?,
        LlamaChatMessage::new("user".into(), user.into())
            .map_err(|e| format!("invalid user prompt: {e}"))?,
    ];
    match model.chat_template(None) {
        Ok(template) => model
            .apply_chat_template(&template, &messages, true)
            .map_err(|e| format!("apply model chat template: {e}")),
        Err(_) => Ok(format!("System: {system}\n\nUser: {user}\n\nAssistant:")),
    }
}

fn make_model_params(gpu_layers: u32) -> LlamaModelParams {
    let params = LlamaModelParams::default();
    #[cfg(any(feature = "cuda", feature = "vulkan"))]
    if gpu_layers > 0 {
        return params.with_n_gpu_layers(gpu_layers);
    }
    #[cfg(not(any(feature = "cuda", feature = "vulkan")))]
    let _ = gpu_layers;
    params
}

pub fn model_status(config: &AppConfig) -> ModelStatus {
    let path = config::model_local_path(&config.model_repo, &config.model_file).ok();
    let downloaded = path.as_ref().is_some_and(|path| path.is_file());
    let loaded = ENGINE.lock().as_ref()
        .and_then(|engine| engine.loaded.as_ref())
        .is_some_and(|model| path.as_ref() == Some(&model.path));
    let backend = current_backend();
    ModelStatus {
        downloaded,
        loaded,
        local_path: path.and_then(|path| path.to_str().map(str::to_owned)),
        repo: config.model_repo.clone(),
        file: config.model_file.clone(),
        gpu_feature: backend.clone(),
        backend,
        devices: detect_devices(),
        gpu_offload_available: cfg!(any(feature = "cuda", feature = "vulkan")),
        max_devices: llama_cpp_2::max_devices() as u32,
    }
}

fn current_backend() -> String {
    #[cfg(feature = "cuda")]
    { return "cuda".into(); }
    #[cfg(all(feature = "vulkan", not(feature = "cuda")))]
    { return "vulkan".into(); }
    #[cfg(not(any(feature = "cuda", feature = "vulkan")))]
    { "cpu".into() }
}

pub fn unload() {
    if let Some(engine) = ENGINE.lock().as_mut() {
        engine.loaded = None;
    }
}

pub fn download_model(repo: &str, file: &str) -> Result<PathBuf, String> {
    let destination = config::model_local_path(repo, file)?;
    if destination.is_file() {
        return Ok(destination);
    }
    config::ensure_parent(&destination)?;
    let api = hf_hub::api::sync::Api::new().map_err(|e| format!("create Hugging Face client: {e}"))?;
    let cached = api.model(repo.to_owned()).get(file)
        .map_err(|e| format!("download {repo}/{file}: {e}"))?;
    let temporary = destination.with_extension("gguf.download");
    fs::copy(&cached, &temporary).map_err(|e| format!("copy downloaded model: {e}"))?;
    fs::rename(&temporary, &destination).map_err(|e| format!("finish model download: {e}"))?;
    Ok(destination)
}

pub fn ensure_loaded(config: &AppConfig) -> Result<(), String> {
    let path = config::model_local_path(&config.model_repo, &config.model_file)?;
    if !path.is_file() {
        return Err(format!("model is not downloaded: {}/{}", config.model_repo, config.model_file));
    }
    let mut guard = ENGINE.lock();
    if guard.is_none() {
        *guard = Some(LlamaEngine::new()?);
    }
    guard.as_mut().expect("engine was initialized").load(&path, config.gpu_layers)
}

pub fn generate(system: &str, user: &str, config: &AppConfig) -> Result<String, String> {
    ensure_loaded(config)?;
    ENGINE.lock().as_mut().expect("engine was initialized").generate(system, user, config)
}

pub fn generate_streaming<F>(
    system: &str,
    user: &str,
    config: &AppConfig,
    on_piece: F,
) -> Result<String, String>
where
    F: FnMut(&str),
{
    ensure_loaded(config)?;
    ENGINE
        .lock()
        .as_mut()
        .expect("engine was initialized")
        .generate_streaming(system, user, config, on_piece)
}

fn detect_devices() -> Vec<BackendDevice> {
    let backend = current_backend();
    vec![BackendDevice {
        id: backend.clone(),
        name: match backend.as_str() { "cuda" => "NVIDIA CUDA", "vulkan" => "Vulkan GPU", _ => "CPU" }.into(),
        description: format!("{backend} inference through llama.cpp"),
        backend,
    }]
}

#[derive(serde::Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct BackendDevice { pub id: String, pub name: String, pub backend: String, pub description: String }

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
