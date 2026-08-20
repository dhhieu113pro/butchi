use anyhow::{anyhow, Result};
use candle_core::quantized::gguf_file;
use candle_core::{Device, Tensor};
use candle_transformers::generation::LogitsProcessor;
use candle_transformers::models::quantized_llama::ModelWeights;
use tokenizers::Tokenizer;
use std::fs::File;
use std::path::Path;
use std::sync::Arc;

pub struct LlamaEngine {
    model: Arc<std::sync::Mutex<ModelWeights>>,
    tokenizer: Tokenizer,
    device: Device,
}

impl LlamaEngine {
    pub fn load_from_file(model_path: &Path, tokenizer_path: &Path) -> Result<Self> {
        if !model_path.exists() {
            return Err(anyhow!("GGUF Model file not found at {:?}", model_path));
        }

        println!("Loading GGUF model from {:?} into Candle engine...", model_path);
        let mut file = File::open(model_path)?;
        let device = Device::Cpu;

        let gguf = gguf_file::Content::read(&mut file)
            .map_err(|e| anyhow!("Failed to read GGUF content: {:?}", e))?;

        let model = ModelWeights::from_gguf(gguf, &mut file, &device)
            .map_err(|e| anyhow!("Failed loading GGUF model weights: {:?}", e))?;

        println!("GGUF Model weights successfully loaded!");

        let tokenizer = if tokenizer_path.exists() {
            Tokenizer::from_file(tokenizer_path)
                .map_err(|e| anyhow!("Failed to load tokenizer.json: {:?}", e))?
        } else {
            return Err(anyhow!("tokenizer.json not found at {:?}", tokenizer_path));
        };

        Ok(Self {
            model: Arc::new(std::sync::Mutex::new(model)),
            tokenizer,
            device,
        })
    }

    pub fn rewrite_text(&self, text: &str, system_prompt: &str) -> Result<String> {
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

        let mut logits_processor = LogitsProcessor::new(299792458, Some(0.7), Some(0.9));
        let mut all_tokens = prompt_tokens.to_vec();
        let mut generated_tokens = Vec::new();

        let mut model_guard = self
            .model
            .lock()
            .map_err(|_| anyhow!("Failed to lock model"))?;

        for pos in 0..prompt_tokens.len() {
            let input = Tensor::new(&[prompt_tokens[pos]], &self.device)?.unsqueeze(0)?;
            let logits = model_guard.forward(&input, pos)?;
            let logits = logits.squeeze(0)?;

            if pos + 1 == prompt_tokens.len() {
                let next_token = logits_processor.sample(&logits)?;
                all_tokens.push(next_token);
                generated_tokens.push(next_token);
            }
        }

        let max_new_tokens = 512;
        for _ in 0..max_new_tokens {
            let pos = all_tokens.len() - 1;
            let last_token = *all_tokens.last().unwrap();

            // Stop tokens (<|im_end|>, <|endoftext|>, etc.)
            if last_token == 151645 || last_token == 151643 || last_token == 2 {
                break;
            }

            let input = Tensor::new(&[last_token], &self.device)?.unsqueeze(0)?;
            let logits = model_guard.forward(&input, pos)?;
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

        Ok(cleaned)
    }
}
