use arboard::Clipboard;

use crate::{config, history, llm};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TextAction {
    Translate,
    Rewrite,
}

impl TextAction {
    pub fn parse(value: &str) -> Result<Self, String> {
        match value.trim().to_ascii_lowercase().as_str() {
            "translate" => Ok(Self::Translate),
            "rewrite" => Ok(Self::Rewrite),
            other => Err(format!("unknown action: {other}")),
        }
    }
}

#[derive(serde::Serialize)]
pub struct ProcessResult {
    pub text: String,
    pub message: String,
    pub copied: bool,
}

fn copy_to_clipboard(text: &str) -> Result<(), String> {
    let mut clipboard =
        Clipboard::new().map_err(|error| format!("clipboard unavailable: {error}"))?;
    clipboard
        .set_text(text.to_owned())
        .map_err(|error| format!("failed to copy result: {error}"))
}

fn rewrite_offline(input: &str) -> String {
    let mut text = input.trim().to_owned();
    if text.is_empty() {
        return text;
    }

    let replacements = [
        ("me and him ", "he and I "),
        ("me and her ", "she and I "),
        ("me and them ", "they and I "),
        ("goes to ", "went to "),
        ("go to store", "go to the store"),
        ("to store ", "to the store "),
        ("to store.", "to the store."),
        ("dont ", "don't "),
        ("doesnt ", "doesn't "),
        ("wont ", "won't "),
        ("cant ", "can't "),
        ("im ", "I'm "),
        ("i ", "I "),
        (" i ", " I "),
    ];

    let lower = text.to_ascii_lowercase();
    for (from, to) in replacements {
        if lower.contains(from) {
            let mut result = String::with_capacity(text.len());
            let mut rest = text.as_str();
            while let Some(index) = rest.to_ascii_lowercase().find(from) {
                result.push_str(&rest[..index]);
                result.push_str(to);
                rest = &rest[index + from.len()..];
            }
            result.push_str(rest);
            text = result;
        }
    }

    let mut chars = text.chars().collect::<Vec<_>>();
    if let Some(pos) = chars.iter().position(|c| c.is_alphabetic()) {
        chars[pos] = chars[pos].to_ascii_uppercase();
        text = chars.into_iter().collect();
    }

    let trimmed = text.trim_end();
    if !trimmed.is_empty()
        && !trimmed.ends_with(['.', '!', '?', '…'])
        && trimmed.chars().filter(|c| c.is_alphabetic()).count() > 8
    {
        text = format!("{trimmed}.");
    }

    text
}

pub fn process(action: TextAction, input: &str, copy: bool) -> Result<ProcessResult, String> {
    let source = input.trim();
    if source.is_empty() {
        return Err("no text to process".into());
    }

    let cfg = config::load();

    match action {
        TextAction::Translate if !cfg.translate_enabled => {
            return Err("Translate is disabled in Settings".into());
        }
        TextAction::Rewrite if !cfg.rewrite_enabled => {
            return Err("Rewrite is disabled in Settings".into());
        }
        _ => {}
    }

    let (text, message) = match action {
        TextAction::Rewrite => rewrite_with_llm_or_offline(source, &cfg),
        TextAction::Translate => translate_with_llm(source, &cfg)?,
    };

    // Persist for History (Settings → History). Best-effort.
    history::append(action, source, &text, &message);

    if !copy {
        return Ok(ProcessResult {
            text,
            message,
            copied: false,
        });
    }

    let copied = match copy_to_clipboard(&text) {
        Ok(()) => true,
        Err(error) => {
            return Ok(ProcessResult {
                text,
                message: format!("{message} (copy failed: {error})"),
                copied: false,
            });
        }
    };

    Ok(ProcessResult {
        text,
        message,
        copied,
    })
}

fn rewrite_with_llm_or_offline(source: &str, cfg: &config::AppConfig) -> (String, String) {
    match llm::generate(&cfg.rewrite_system_prompt, source, cfg) {
        Ok(text) if !text.trim().is_empty() => (
            text,
            format!(
                "Rewritten with local LLM ({}). Result copied.",
                cfg.model_file
            ),
        ),
        Ok(_) => {
            let rewritten = rewrite_offline(source);
            (
                rewritten,
                "LLM returned empty output — used offline rewrite. Result copied.".into(),
            )
        }
        Err(error) => {
            let rewritten = rewrite_offline(source);
            (
                rewritten,
                format!("LLM unavailable ({error}). Offline rewrite used. Result copied."),
            )
        }
    }
}

fn translate_with_llm(
    source: &str,
    cfg: &config::AppConfig,
) -> Result<(String, String), String> {
    let system = format!(
        "{}\n\nTarget language: {}.",
        cfg.translate_system_prompt, cfg.target_language
    );

    match llm::generate(&system, source, cfg) {
        Ok(text) if !text.trim().is_empty() => Ok((
            text,
            format!(
                "Translated to {} with local LLM. Result copied.",
                cfg.target_language
            ),
        )),
        Ok(_) => Err("LLM returned empty translation. Is the model downloaded?".into()),
        Err(error) => Err(format!(
            "Translation needs the local model. {error}"
        )),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rewrite_fixes_common_spoken_english() {
        let out = rewrite_offline("me and him goes to store yesterday");
        assert!(out.to_ascii_lowercase().contains("he and i"));
        assert!(out.chars().next().is_some_and(|c| c.is_uppercase()));
    }

    #[test]
    fn empty_input_is_rejected() {
        assert!(process(TextAction::Rewrite, "   ", false).is_err());
    }
}
