use anyhow::{anyhow, Result};
use candle_core::quantized::gguf_file;
use candle_core::{Device, Tensor};
use candle_transformers::generation::LogitsProcessor;
use candle_transformers::models::quantized_qwen2::ModelWeights;
use tokenizers::Tokenizer;
use std::fs::File;
use std::path::Path;
use std::sync::Mutex;

pub struct LlamaEngine {
    model: Mutex<ModelWeights>,
    tokenizer: Tokenizer,
    device: Device,
}

impl LlamaEngine {
    pub fn load_from_file(model_path: &Path, tokenizer_path: &Path) -> Result<Self> {
        if !model_path.exists() {
            return Err(anyhow!("GGUF Model file not found at {:?}", model_path));
        }

        let device = Device::new_cuda(0).unwrap_or_else(|e| {
            println!("ℹ️ CUDA GPU notice: {}, using CPU device", e);
            Device::Cpu
        });

        println!("Loading Qwen2 GGUF model weights into RAM/VRAM on device {:?}...", device);
        let mut file = File::open(model_path)?;
        let gguf = gguf_file::Content::read(&mut file)
            .map_err(|e| anyhow!("Failed to read GGUF content: {:?}", e))?;

        let model_weights = ModelWeights::from_gguf(gguf, &mut file, &device)
            .map_err(|e| anyhow!("Failed loading GGUF model weights: {:?}", e))?;

        let tokenizer = if tokenizer_path.exists() {
            Tokenizer::from_file(tokenizer_path)
                .map_err(|e| anyhow!("Failed to load tokenizer.json: {:?}", e))?
        } else {
            return Err(anyhow!("tokenizer.json not found at {:?}", tokenizer_path));
        };

        println!("✅ GGUF Model weights successfully loaded into memory! (Device: {:?})", device);
        Ok(Self {
            model: Mutex::new(model_weights),
            tokenizer,
            device,
        })
    }

    pub fn rewrite_text(&self, text: &str, system_prompt: &str) -> Result<String> {
        println!("[LLM Engine] Fast rewrite starting for: {:?}", text);
        let prompt = format!(
            "<|im_start|>system\n{}<|im_end|>\n<|im_start|>user\n{}<|im_end|>\n<|im_start|>assistant\n",
            system_prompt, text
        );

        let tokens = self
            .tokenizer
            .encode(prompt, true)
            .map_err(|e| anyhow!("Tokenization failed: {:?}", e))?;

        let prompt_tokens = tokens.get_ids();
        if prompt_tokens.is_empty() {
            return Err(anyhow!("Empty prompt tokens"));
        }

        let mut model_weights = self.model.lock().map_err(|_| anyhow!("Failed to lock model"))?;

        // Process all prompt tokens in a SINGLE batched forward pass!
        let input = Tensor::new(prompt_tokens, &self.device)?.unsqueeze(0)?;
        let logits = model_weights.forward(&input, 0)?;
        let logits = logits.squeeze(0)?;

        let mut logits_processor = LogitsProcessor::new(299792458, Some(0.7), Some(0.9));
        let mut all_tokens = prompt_tokens.to_vec();
        let mut generated_tokens = Vec::new();

        let next_token = logits_processor.sample(&logits)?;
        all_tokens.push(next_token);
        generated_tokens.push(next_token);

        let max_new_tokens = 64;
        for _ in 0..max_new_tokens {
            let pos = all_tokens.len() - 1;
            let last_token = *all_tokens.last().unwrap();

            // Qwen stop tokens (<|im_end|>, <|endoftext|>)
            if last_token == 151645 || last_token == 151643 || last_token == 2 {
                break;
            }

            let input = Tensor::new(&[last_token], &self.device)?.unsqueeze(0)?;
            let logits = model_weights.forward(&input, pos)?;
            let logits = logits.squeeze(0)?;

            let next_token = logits_processor.sample(&logits)?;
            if next_token == 151645 || next_token == 151643 || next_token == 2 {
                break;
            }

            all_tokens.push(next_token);
            generated_tokens.push(next_token);
        }

        let decoded = self
            .tokenizer
            .decode(&generated_tokens, true)
            .map_err(|e| anyhow!("Decode error: {:?}", e))?;

        let cleaned = decoded
            .trim()
            .trim_start_matches('"')
            .trim_end_matches('"')
            .trim()
            .to_string();

        println!("✨ [LLM Engine] Output: {:?}", cleaned);
        Ok(cleaned)
    }
}
