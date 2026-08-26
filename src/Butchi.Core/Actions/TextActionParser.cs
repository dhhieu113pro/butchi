namespace Butchi.Core.Actions;

public static class TextActionParser
{
    public static TextAction Parse(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "translate" => TextAction.Translate,
            "rewrite" => TextAction.Rewrite,
            var other => throw new ArgumentException($"unknown action: {other}", nameof(value))
        };
}
