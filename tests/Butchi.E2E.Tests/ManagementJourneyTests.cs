using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace Butchi.E2E.Tests;

public sealed class ManagementJourneyTests
{
    [Fact]
    public void User_can_navigate_core_management_pages()
    {
        var appPath = Environment.GetEnvironmentVariable("BUTCHI_E2E_APP");
        if (string.IsNullOrWhiteSpace(appPath))
            return;

        Assert.True(File.Exists(appPath), $"Butchi executable missing: {appPath}");

        using var app = Application.Launch(appPath, "--e2e");
        using var automation = new UIA3Automation();
        try
        {
            var window = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Butchi Settings"),
                TimeSpan.FromSeconds(15)).Result;
            Assert.NotNull(window);

            var cf = new ConditionFactory(new UIA3PropertyLibrary());
            Click(window, cf, "NavPrompts");
            Click(window, cf, "NavModel");
            Click(window, cf, "NavHistory");
            Click(window, cf, "NavAboutPrivacy");
            Click(window, cf, "NavGeneral");

            Assert.Equal("Butchi Settings", window.Title);
        }
        finally
        {
            if (!app.HasExited)
                app.Kill();
        }
    }

    private static void Click(FlaUI.Core.AutomationElements.Window window, ConditionFactory cf, string automationId)
    {
        var button = Retry.WhileNull(
            () => window.FindFirstDescendant(cf.ByAutomationId(automationId))?.AsButton(),
            TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(button);
        button.Invoke();
    }
}
