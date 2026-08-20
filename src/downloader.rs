use anyhow::{anyhow, Result};
use futures_util::StreamExt;
use std::fs;
use std::path::Path;
use tokio::fs::File;
use tokio::io::AsyncWriteExt;

pub async fn ensure_file_downloaded(
    file_path: &Path,
    download_url: &str,
    mut progress_cb: impl FnMut(u64, Option<u64>),
) -> Result<()> {
    let client = reqwest::Client::new();
    let response = client
        .get(download_url)
        .header("User-Agent", "rust-rewrite-app/1.0")
        .send()
        .await?;

    if !response.status().is_success() {
        return Err(anyhow!(
            "Failed to download file: HTTP status {}",
            response.status()
        ));
    }

    let total_size = response.content_length();

    // Check existing file size
    if file_path.exists() {
        if let Ok(metadata) = fs::metadata(file_path) {
            if let Some(expected) = total_size {
                if metadata.len() == expected {
                    return Ok(());
                }
                println!("Incomplete file found ({}/{} bytes). Re-downloading...", metadata.len(), expected);
                let _ = fs::remove_file(file_path);
            } else if metadata.len() > 0 {
                return Ok(());
            }
        }
    }

    if let Some(parent) = file_path.parent() {
        fs::create_dir_all(parent)?;
    }

    println!("Downloading file from: {}", download_url);

    let mut file = File::create(file_path).await?;
    let mut downloaded: u64 = 0;
    let mut stream = response.bytes_stream();

    while let Some(chunk_result) = stream.next().await {
        let chunk = chunk_result?;
        file.write_all(&chunk).await?;
        downloaded += chunk.len() as u64;
        progress_cb(downloaded, total_size);
    }

    file.flush().await?;
    println!("\nFile saved successfully to: {:?}", file_path);
    Ok(())
}

pub async fn ensure_model_and_tokenizer(
    model_path: &Path,
    tokenizer_path: &Path,
    hf_repo: &str,
    hf_filename: &str,
    progress_cb: impl FnMut(u64, Option<u64>) + Copy,
) -> Result<()> {
    let model_url = format!(
        "https://huggingface.co/{}/resolve/main/{}",
        hf_repo, hf_filename
    );
    ensure_file_downloaded(model_path, &model_url, progress_cb).await?;

    if !tokenizer_path.exists() {
        let tokenizer_url = format!(
            "https://huggingface.co/{}/resolve/main/tokenizer.json",
            hf_repo.replace("-GGUF", "")
        );
        let _ = ensure_file_downloaded(tokenizer_path, &tokenizer_url, |_, _| {}).await;
    }

    Ok(())
}
