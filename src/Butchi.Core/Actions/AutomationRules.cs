using Butchi.Core.Configuration;

namespace Butchi.Core.Actions;

public static class AutomationRules
{
    public static IReadOnlyList<TextAction> GetEnabledActions(AppConfig config)
    {
        var actions = new List<TextAction>(2);
        if (config.TranslateEnabled)
        {
            actions.Add(TextAction.Translate);
        }

        if (config.RewriteEnabled)
        {
            actions.Add(TextAction.Rewrite);
        }

        return actions;
    }

    public static bool ShouldApplyAutomaticResult(AppConfig config) =>
        GetEnabledActions(config).Count == 1 && config.ResultAction != ResultAction.None;
}
