use arboard::Clipboard;

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

/// Lightweight offline rewrite aimed at common selection fixes.
/// Real LLM / provider wiring belongs in a later milestone.
fn rewrite_offline(input: &str) -> String {
    let mut text = input.trim().to_owned();
    if text.is_empty() {
        return text;
    }

    // Common spoken / chat patterns
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
            // Case-insensitive replace while keeping a simple implementation.
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

    // Capitalize first alphabetic character.
    let mut chars = text.chars().collect::<Vec<_>>();
    if let Some(pos) = chars.iter().position(|c| c.is_alphabetic()) {
        chars[pos] = chars[pos].to_ascii_uppercase();
        text = chars.into_iter().collect();
    }

    // Ensure terminal punctuation for short sentences.
    let trimmed = text.trim_end();
    if !trimmed.is_empty()
        && !trimmed.ends_with(['.', '!', '?', '…'])
        && trimmed.chars().filter(|c| c.is_alphabetic()).count() > 8
    {
        text = format!("{trimmed}.");
    }

    text
}

pub fn process(action: TextAction, input: &str) -> Result<ProcessResult, String> {
    let source = input.trim();
    if source.is_empty() {
        return Err("no text to process".into());
    }

    let (text, message) = match action {
        TextAction::Rewrite => {
            let rewritten = rewrite_offline(source);
            let message = if rewritten == source {
                "Rewrite complete (no changes). Result copied.".to_owned()
            } else {
                "Rewritten offline. Result copied to clipboard.".to_owned()
            };
            (rewritten, message)
        }
        TextAction::Translate => {
            // Provider not connected yet — still copy so the user can paste into any translator.
            (
                source.to_owned(),
                "Translation provider not connected yet. Original text copied — paste into your translator."
                    .to_owned(),
            )
        }
    };

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
        assert!(process(TextAction::Rewrite, "   ").is_err());
    }
}
