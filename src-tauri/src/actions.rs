use arboard::Clipboard;

use crate::{config, core_logic, history, llm};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TextAction {
    Translate,
    Rewrite,
}

impl TextAction {
    pub fn parse(value: &str) -> Result<Self, String> {
        match core_logic::parse_action(value)? {
            "translate" => Ok(Self::Translate),
            "rewrite" => Ok(Self::Rewrite),
            _ => unreachable!("core action parser returned an unsupported action"),
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
    let mut clipboard = Clipboard::new().map_err(|error| format!("clipboard unavailable: {error}"))?;
    clipboard.set_text(text.to_owned()).map_err(|error| format!("failed to copy result: {error}"))
}

fn action_name(action: TextAction) -> &'static str {
    match action {
        TextAction::Translate => "translate",
        TextAction::Rewrite => "rewrite",
    }
}

fn validate_action(action: TextAction, cfg: &config::AppConfig) -> Result<(), String> {
    core_logic::validate_action_enabled(action_name(action), cfg.translate_enabled, cfg.rewrite_enabled)
}

fn strip_reasoning(text: &str) -> String {
    let mut output = String::with_capacity(text.len());
    let mut rest = text;

    loop {
        let lower = rest.to_ascii_lowercase();
        let Some(start) = lower.find("<think>") else {
            output.push_str(rest);
            break;
        };

        output.push_str(&rest[..start]);
        let after_open = &rest[start + "<think>".len()..];
        let after_lower = after_open.to_ascii_lowercase();

        let Some(end) = after_lower.find("</think>") else {
            break;
        };

        rest = &after_open[end + "</think>".len()..];
    }

    output.trim().to_owned()
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
        return ProcessResult { text, message, copied: false };
    }

    match copy_to_clipboard(&text) {
        Ok(()) => ProcessResult { text, message, copied: true },
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
        TextAction::Rewrite => match llm::generate_streaming(
            &cfg.rewrite_system_prompt,
            source,
            &cfg,
            |piece| on_piece(piece),
        ) {
            Ok(text) => {
                let cleaned = strip_reasoning(&text);
                if cleaned.is_empty() {
                    let rewritten = core_logic::rewrite_offline(source);
                    on_piece(&rewritten);
                    (
                        rewritten,
                        "LLM returned empty output — used offline rewrite. Result copied.".into(),
                    )
                } else {
                    (
                        cleaned,
                        format!("Rewritten with local LLM ({}). Result copied.", cfg.model_file),
                    )
                }
            }
            Err(error) => {
                let rewritten = core_logic::rewrite_offline(source);
                on_piece(&rewritten);
                (
                    rewritten,
                    format!("LLM unavailable ({error}). Offline rewrite used. Result copied."),
                )
            }
        },
        TextAction::Translate => {
            let system = format!(
                "{}\n\nTarget language: {}.",
                cfg.translate_system_prompt, cfg.target_language
            );
            match llm::generate_streaming(&system, source, &cfg, |piece| on_piece(piece)) {
                Ok(text) => {
                    let cleaned = strip_reasoning(&text);
                    if cleaned.is_empty() {
                        return Err("LLM returned empty translation. Is the model downloaded?".into());
                    }
                    (
                        cleaned,
                        format!("Translated to {} with local LLM. Result copied.", cfg.target_language),
                    )
                }
                Err(error) => return Err(format!("Translation needs the local model. {error}")),
            }
        }
    };

    Ok(finish_result(action, source, text, message, copy, &cfg))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn text_action_parser_covers_translate_rewrite_and_unknown() {
        assert_eq!(TextAction::parse("translate").unwrap(), TextAction::Translate);
        assert_eq!(TextAction::parse(" Rewrite ").unwrap(), TextAction::Rewrite);
        assert!(TextAction::parse("summarize").is_err());
    }

    #[test]
    fn validation_covers_enabled_and_disabled_actions() {
        let mut cfg = config::AppConfig::default();
        assert!(validate_action(TextAction::Translate, &cfg).is_ok());
        assert!(validate_action(TextAction::Rewrite, &cfg).is_ok());
        cfg.translate_enabled = false;
        assert!(validate_action(TextAction::Translate, &cfg).is_err());
        cfg.translate_enabled = true;
        cfg.rewrite_enabled = false;
        assert!(validate_action(TextAction::Rewrite, &cfg).is_err());
    }

    #[test]
    fn action_names_are_stable() {
        assert_eq!(action_name(TextAction::Translate), "translate");
        assert_eq!(action_name(TextAction::Rewrite), "rewrite");
    }

    #[test]
    fn empty_input_is_rejected_without_needing_a_model() {
        assert!(process(TextAction::Rewrite, "   ", false).is_err());
    }

    #[test]
    fn hidden_reasoning_is_removed_from_model_output() {
        assert_eq!(
            strip_reasoning("<think>private reasoning</think>Visible answer"),
            "Visible answer"
        );
        assert_eq!(
            strip_reasoning("Before<think>private reasoning</think>After"),
            "BeforeAfter"
        );
        assert_eq!(strip_reasoning("Visible answer"), "Visible answer");
    }
}
