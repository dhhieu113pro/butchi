using Butchi.Core.Actions;
using Butchi.Core.Configuration;

namespace Butchi.Core.Prompts;

public static class PromptBuilder
{
    public static string Build(TextAction action, string input, AppConfig config, bool suppressThinking = false)
    {
        var source = input.Trim();
        if (source.Length == 0)
        {
            throw new ArgumentException("no text to process", nameof(input));
        }

        var targetLanguage = action == TextAction.Translate
            ? AppConfig.NormalizeTargetLanguage(config.TargetLanguage)
            : null;

        var system = action switch
        {
            TextAction.Translate => $"{config.TranslateSystemPrompt}\n\nTarget language: {targetLanguage}.",
            TextAction.Rewrite => config.RewriteSystemPrompt,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        if (suppressThinking)
        {
            system = $"{system}\n\n/no_think";
        }

        var user = action switch
        {
            TextAction.Translate => $"TARGET LANGUAGE: {targetLanguage}\n\nSOURCE:\n<source>\n{source}\n</source>\n\nTranslate SOURCE now. Output only the translation.",
            TextAction.Rewrite => source,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        return $"<|im_start|>system\n{system}<|im_end|>\n<|im_start|>user\n{user}<|im_end|>\n<|im_start|>assistant\n";
    }
}
