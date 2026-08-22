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

fn validate_action(action: TextAction, cfg: &config::AppConfig) -> Result<(), String> {
    match action {
        TextAction::Translate if !cfg.translate_enabled => {
            Err("Translate is disabled in Settings".into())
        }
        TextAction::Rewrite if !cfg.rewrite_enabled => {
            Err("Rewrite is disabled in Settings".into())
        }
        _ => Ok(()),
    }
}

fn finish_result(
    action: TextAction,
    source: &str,
    text: String,
    message: String,
    copy: bool,
    cfg: &config::AppConfig,
) -> ProcessResult {
    let target_language = match action {
        TextAction::Translate => Some(cfg.target_language.as_str()),
        TextAction::Rewrite => None,
    };
    history::append(action, source, &text, &message, target_language);

    if !copy {
        return ProcessResult {
            text,
            message,
            copied: false,
        };
    }

    match copy_to_clipboard(&text) {
        Ok(()) => ProcessResult {
            text,
            message,
            copied: true,
        },
        Err(error) => ProcessResult {
            text,
            message: format!("{message} (copy failed: {error})"),
            copied: false,
        },
    }
}

pub fn process(action: TextAction, input: &str, copy: bool) -> Result<ProcessResult, String> {
    process_stream(action, input, copy, |_| {})
}

pub fn process_stream<F>(
    action: TextAction,
    input: &str,
    copy: bool,
    mut on_piece: F,
) -> Result<ProcessResult, String>
where
    F: FnMut(&str),
{
    let source = input.trim();
    if source.is_empty() {
        return Err("no text to process".into());
    }

    let cfg = config::load();
    validate_action(action, &cfg)?;

    let (text, message) = match action {
        TextAction::Rewrite => {
            match llm::generate_streaming(&cfg.rewrite_system_prompt, source, &cfg, |piece| {
                on_piece(piece)
            }) {
                Ok(text) if !text.trim().is_empty() => (
                    text,
                    format!("Rewritten with local LLM ({}). Result copied.", cfg.model_file),
                ),
                Ok(_) => {
                    let rewritten = rewrite_offline(source);
                    on_piece(&rewritten);
                    (
                        rewritten,
                        "LLM returned empty output — used offline rewrite. Result copied.".into(),
                    )
                }
                Err(error) => {
                    let rewritten = rewrite_offline(source);
                    on_piece(&rewritten);
                    (
                        rewritten,
                        format!("LLM unavailable ({error}). Offline rewrite used. Result copied."),
                    )
                }
            }
        }
        TextAction::Translate => {
            let system = format!(
                "{}\n\nTarget language: {}.",
                cfg.translate_system_prompt, cfg.target_language
            );
            match llm::generate_streaming(&system, source, &cfg, |piece| on_piece(piece)) {
                Ok(text) if !text.trim().is_empty() => (
                    text,
                    format!("Translated to {} with local LLM. Result copied.", cfg.target_language),
                ),
                Ok(_) => return Err("LLM returned empty translation. Is the model downloaded?".into()),
                Err(error) => {
                    return Err(format!("Translation needs the local model. {error}"));
                }
            }
        }
    };

    Ok(finish_result(action, source, text, message, copy, &cfg))
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
