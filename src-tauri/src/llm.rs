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

    ModelStatus {
        downloaded,
        loaded,
        local_path: path.and_then(|p| p.to_str().map(str::to_owned)),
        repo: config.model_repo.clone(),
        file: config.model_file.clone(),
        gpu_feature: current_gpu_feature(),
    }
}

fn current_gpu_feature() -> String {
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

#[derive(serde::Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct ModelStatus {
    pub downloaded: bool,
    pub loaded: bool,
    pub local_path: Option<String>,
    pub repo: String,
    pub file: String,
    pub gpu_feature: String,
}

pub fn download_model(repo: &str, file: &str) -> Result<PathBuf, String> {
    let dest = config::model_local_path(repo, file)?;
    if dest.is_file() {
        return Ok(dest);
    }
    config::ensure_parent(&dest)?;

    let api = hf_hub::api::sync::ApiBuilder::new()
        .with_progress(true)
        .build()
        .map_err(|e| format!("huggingface api: {e}"))?;

    let cached = api
        .model(repo.to_owned())
        .get(file)
        .map_err(|e| format!("download {repo}/{file}: {e}"))?;

    if cached != dest {
        fs::copy(&cached, &dest).map_err(|e| format!("copy model into app dir: {e}"))?;
    }
    Ok(dest)
}

pub fn unload() {
    *ENGINE.lock() = None;
}

pub fn ensure_loaded(config: &AppConfig) -> Result<(), String> {
    let path = config::model_local_path(&config.model_repo, &config.model_file)?;
    if !path.is_file() {
        return Err(format!(
            "model not downloaded yet: {} / {}. Open Settings and download it.",
            config.model_repo, config.model_file
        ));
    }

    {
        let guard = ENGINE.lock();
        if guard.as_ref().is_some_and(|m| m.path == path) {
            return Ok(());
        }
    }

    ensure_backend()?;
    let model = {
        let backend_guard = BACKEND.lock();
        let backend = backend_guard
            .as_ref()
            .ok_or_else(|| "backend missing".to_string())?;
        load_model(backend, &path, config.gpu_layers)?
    };

    *ENGINE.lock() = Some(LoadedModel { path, model });
    Ok(())
}

fn load_model(backend: &LlamaBackend, path: &Path, gpu_layers: u32) -> Result<LlamaModel, String> {
    #[cfg(any(feature = "cuda", feature = "vulkan"))]
    let params = LlamaModelParams::default().with_n_gpu_layers(gpu_layers);
    #[cfg(not(any(feature = "cuda", feature = "vulkan")))]
    let params = {
        let _ = gpu_layers;
        LlamaModelParams::default()
    };

    LlamaModel::load_from_file(backend, path, &params)
        .map_err(|e| format!("load model {}: {e}", path.display()))
}

pub fn generate(system: &str, user: &str, config: &AppConfig) -> Result<String, String> {
    ensure_loaded(config)?;
    ensure_backend()?;

    let max_tokens = config.max_tokens.max(16) as i32;
    let temperature = config.temperature.clamp(0.0, 2.0);

    // Lock order: backend then engine (never reverse).
    let backend_guard = BACKEND.lock();
    let backend = backend_guard
        .as_ref()
        .ok_or_else(|| "backend missing".to_string())?;
    let engine_guard = ENGINE.lock();
    let loaded = engine_guard
        .as_ref()
        .ok_or_else(|| "model not loaded".to_string())?;

    let prompt = build_prompt(&loaded.model, system, user);
    run_completion(backend, &loaded.model, &prompt, max_tokens, temperature)
}

fn build_prompt(model: &LlamaModel, system: &str, user: &str) -> String {
    if let Ok(tmpl) = model.chat_template(None) {
        if let (Ok(system_msg), Ok(user_msg)) = (
            LlamaChatMessage::new("system".into(), system.into()),
            LlamaChatMessage::new("user".into(), user.into()),
        ) {
            if let Ok(prompt) = model.apply_chat_template(&tmpl, &[system_msg, user_msg], true) {
                return prompt;
            }
        }
    }

    format!(
        "<|im_start|>system\n{system}<|im_end|>\n<|im_start|>user\n{user}<|im_end|>\n<|im_start|>assistant\n"
    )
}

fn run_completion(
    backend: &LlamaBackend,
    model: &LlamaModel,
    prompt: &str,
    n_len: i32,
    temperature: f32,
) -> Result<String, String> {
    let ctx_params = LlamaContextParams::default()
        .with_n_ctx(NonZeroU32::new(4096))
        .with_n_batch(512);

    let mut ctx = model
        .new_context(backend, ctx_params)
        .map_err(|e| format!("new context: {e}"))?;

    let tokens = model
        .str_to_token(prompt, AddBos::Always)
        .map_err(|e| format!("tokenize: {e}"))?;
    if tokens.is_empty() {
        return Err("empty prompt tokens".into());
    }

    let mut batch = LlamaBatch::new(512, 1);
    let last_index = tokens.len() as i32 - 1;
    for (i, token) in (0_i32..).zip(tokens.into_iter()) {
        let is_last = i == last_index;
        batch
            .add(token, i, &[0], is_last)
            .map_err(|e| format!("batch add: {e}"))?;
    }
    ctx.decode(&mut batch)
        .map_err(|e| format!("decode prompt: {e}"))?;

    let mut sampler = if temperature <= 0.05 {
        LlamaSampler::chain_simple([LlamaSampler::greedy()])
    } else {
        LlamaSampler::chain_simple([
            LlamaSampler::temp(temperature),
            LlamaSampler::top_p(0.9, 1),
            LlamaSampler::dist(42),
        ])
    };

    let mut output = String::new();
    let mut n_cur = batch.n_tokens();

    for _ in 0..n_len {
        let token = sampler.sample(&ctx, batch.n_tokens() - 1);
        sampler.accept(token);

        if model.is_eog_token(token) {
            break;
        }

        match model.token_to_piece(token) {
            Ok(piece) => output.push_str(&piece),
            Err(_) => {
                if let Ok(piece) =
                    model.token_to_str(token, llama_cpp_2::model::Special::Tokenize)
                {
                    output.push_str(&piece);
                }
            }
        }

        batch.clear();
        batch
            .add(token, n_cur, &[0], true)
            .map_err(|e| format!("batch add token: {e}"))?;
        n_cur += 1;
        ctx.decode(&mut batch)
            .map_err(|e| format!("decode token: {e}"))?;
    }

    Ok(clean_output(&output))
}

fn clean_output(text: &str) -> String {
    text.trim()
        .trim_matches(|c| c == '"' || c == '“' || c == '”')
        .trim()
        .to_owned()
}
