using Butchi.Core.Actions;
using Butchi.Core.Configuration;

namespace Butchi.Core.Prompts;

public static class PromptBuilder
{
    public static string Build(TextAction action, string input, AppConfig config)
    {
        var source = input.Trim();
        if (source.Length == 0)
        {
            throw new ArgumentException("no text to process", nameof(input));
        }

        var system = action switch
        {
            TextAction.Translate => $"{config.TranslateSystemPrompt}\n\nTarget language: {AppConfig.NormalizeTargetLanguage(config.TargetLanguage)}.",
            TextAction.Rewrite => config.RewriteSystemPrompt,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        return $"<|im_start|>system\n{system}<|im_end|>\n<|im_start|>user\n{source}<|im_end|>\n<|im_start|>assistant\n";
    }
}
