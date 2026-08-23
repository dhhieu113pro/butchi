/// Deterministic application logic kept separate from OS, storage and llama.cpp
/// integration so it can be exhaustively unit-tested.

pub fn parse_action(value: &str) -> Result<&'static str, String> {
    match value.trim().to_ascii_lowercase().as_str() {
        "translate" => Ok("translate"),
        "rewrite" => Ok("rewrite"),
        other => Err(format!("unknown action: {other}")),
    }
}

pub fn validate_action_enabled(action: &str, translate_enabled: bool, rewrite_enabled: bool) -> Result<(), String> {
    match action {
        "translate" if !translate_enabled => Err("Translate is disabled in Settings".into()),
        "rewrite" if !rewrite_enabled => Err("Rewrite is disabled in Settings".into()),
        _ => Ok(()),
    }
}

pub fn normalize_target_language(language: &str) -> Result<String, String> {
    let language = language.trim();
    if language.is_empty() { Err("target language cannot be empty".into()) } else { Ok(language.to_owned()) }
}

pub fn normalize_backend_preference(value: &str) -> &'static str {
    match value.trim().to_ascii_lowercase().as_str() {
        "cpu" => "cpu",
        "gpu" => "gpu",
        _ => "auto",
    }
}

pub fn validate_replace_target(target: usize, foreground: usize) -> Result<(), String> {
    if target == 0 {
        Err("the original selected-text window is no longer available".into())
    } else if foreground != target {
        Err("the original app is no longer active; select the text again before replacing".into())
    } else {
        Ok(())
    }
}

pub fn truncate_text(value: &str, max_chars: usize) -> String {
    if value.chars().count() <= max_chars { value.to_owned() } else { value.chars().take(max_chars).collect::<String>() + "…" }
}

pub fn rewrite_offline(input: &str) -> String {
    let mut text = input.trim().to_owned();
    if text.is_empty() { return text; }

    let replacements = [
        ("me and him ", "he and I "), ("me and her ", "she and I "), ("me and them ", "they and I "),
        ("goes to ", "went to "), ("go to store", "go to the store"), ("to store ", "to the store "),
        ("to store.", "to the store."), ("dont ", "don't "), ("doesnt ", "doesn't "),
        ("wont ", "won't "), ("cant ", "can't "), ("im ", "I'm "), ("i ", "I "), (" i ", " I "),
    ];

    for (from, to) in replacements {
        let lower = text.to_ascii_lowercase();
        if !lower.contains(from) { continue; }
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

    let mut chars = text.chars().collect::<Vec<_>>();
    if let Some(pos) = chars.iter().position(|c| c.is_alphabetic()) {
        chars[pos] = chars[pos].to_ascii_uppercase();
        text = chars.into_iter().collect();
    }

    let trimmed = text.trim_end();
    if !trimmed.is_empty() && !trimmed.ends_with(['.', '!', '?', '…']) && trimmed.chars().filter(|c| c.is_alphabetic()).count() > 8 {
        text = format!("{trimmed}.");
    }
    text
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn action_parsing_covers_supported_and_unknown_values() {
        assert_eq!(parse_action(" Translate ").unwrap(), "translate");
        assert_eq!(parse_action("REWRITE").unwrap(), "rewrite");
        assert_eq!(parse_action("summarize").unwrap_err(), "unknown action: summarize");
    }

    #[test]
    fn action_enablement_covers_all_paths() {
        assert!(validate_action_enabled("translate", true, true).is_ok());
        assert_eq!(validate_action_enabled("translate", false, true).unwrap_err(), "Translate is disabled in Settings");
        assert_eq!(validate_action_enabled("rewrite", true, false).unwrap_err(), "Rewrite is disabled in Settings");
        assert!(validate_action_enabled("rewrite", true, true).is_ok());
    }

    #[test]
    fn target_language_is_trimmed_or_rejected() {
        assert_eq!(normalize_target_language("  Japanese  ").unwrap(), "Japanese");
        assert_eq!(normalize_target_language("  ").unwrap_err(), "target language cannot be empty");
    }

    #[test]
    fn backend_preference_normalizes_every_class() {
        assert_eq!(normalize_backend_preference(" CPU "), "cpu");
        assert_eq!(normalize_backend_preference("GPU"), "gpu");
        assert_eq!(normalize_backend_preference("anything"), "auto");
    }

    #[test]
    fn replace_target_validation_covers_every_outcome() {
        assert!(validate_replace_target(42, 42).is_ok());
        assert!(validate_replace_target(0, 42).unwrap_err().contains("no longer available"));
        assert!(validate_replace_target(42, 99).unwrap_err().contains("no longer active"));
    }

    #[test]
    fn truncation_covers_short_long_unicode_and_zero_limit() {
        assert_eq!(truncate_text("hello", 10), "hello");
        assert_eq!(truncate_text("abcdef", 3), "abc…");
        assert_eq!(truncate_text("Việt Nam", 4), "Việt…");
        assert_eq!(truncate_text("x", 0), "…");
    }

    #[test]
    fn offline_rewrite_covers_empty_replacements_capitalization_and_punctuation() {
        assert_eq!(rewrite_offline("   "), "");
        assert_eq!(rewrite_offline("ok!"), "Ok!");
        assert_eq!(rewrite_offline("123"), "123");
        assert_eq!(rewrite_offline("abcdefghij"), "Abcdefghij.");
        assert_eq!(rewrite_offline("abcdefghij?"), "Abcdefghij?");
        assert_eq!(rewrite_offline("i i i am ready now"), "I I I am ready now.");

        let cases = [
            ("me and him goes to store yesterday", "He and I went to the store yesterday."),
            ("me and her go to store tomorrow", "She and I go to the store tomorrow."),
            ("me and them go to store tomorrow", "They and I go to the store tomorrow."),
            ("dont do that please", "Don't do that please."),
            ("he doesnt understand this", "He doesn't understand this."),
            ("we wont forget this", "We won't forget this."),
            ("i cant finish this", "I can't finish this."),
            ("im going to store now", "I'm going to the store now."),
            ("walk to store please", "Walk to the store please."),
            ("walk to store.", "Walk to the store."),
        ];
        for (input, expected) in cases { assert_eq!(rewrite_offline(input), expected); }
    }
}
