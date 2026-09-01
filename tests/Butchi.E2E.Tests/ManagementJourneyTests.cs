using FlaUI.Core;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace Butchi.E2E.Tests;

public sealed class ManagementJourneyTests
{
    [Fact]
    public void User_can_navigate_settings_and_toggle_actions()
    {
        var appPath = Environment.GetEnvironmentVariable("BUTCHI_E2E_APP");
        Assert.False(string.IsNullOrWhiteSpace(appPath));
        Assert.True(File.Exists(appPath), $"Butchi executable missing: {appPath}");

        using var app = Application.Launch($"\"{appPath}\" --e2e");
        using var automation = new UIA3Automation();
        try
        {
            var window = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Butchi Settings"),
                TimeSpan.FromSeconds(15)).Result;
            Assert.NotNull(window);

            var cf = new ConditionFactory(new UIA3PropertyLibrary());
            Assert.NotNull(window.FindFirstDescendant(cf.ByAutomationId("GeneralPage")));

            Click(window, cf, "NavPrompts");
            Assert.NotNull(window.FindFirstDescendant(cf.ByAutomationId("PromptsPage")));

            Click(window, cf, "NavGeneral");
            var translate = window.FindFirstDescendant(cf.ByAutomationId("TranslateToggle"))?.AsToggleButton();
            var rewrite = window.FindFirstDescendant(cf.ByAutomationId("RewriteToggle"))?.AsToggleButton();
            Assert.NotNull(translate);
            Assert.NotNull(rewrite);

            var translateBefore = translate.ToggleState;
            translate.Toggle();
            Assert.NotEqual(translateBefore, translate.ToggleState);

            var rewriteBefore = rewrite.ToggleState;
            rewrite.Toggle();
            Assert.NotEqual(rewriteBefore, rewrite.ToggleState);

            Click(window, cf, "NavHistory");
            Assert.NotNull(window.FindFirstDescendant(cf.ByAutomationId("HistoryPage")));
        }
        finally
        {
            if (!app.HasExited)
                app.Kill();
        }
    }

    private static void Click(FlaUI.Core.AutomationElements.Window window, ConditionFactory cf, string automationId)
    {
        var button = window.FindFirstDescendant(cf.ByAutomationId(automationId))?.AsButton();
        Assert.NotNull(button);
        button.Invoke();
    }
}
