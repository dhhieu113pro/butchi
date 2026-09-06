using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace Butchi.E2E.Tests;

public sealed class FirstRunJourneyTests
{
    [Fact]
    public void First_run_downloads_model_before_finishing_setup()
    {
        var appPath = Environment.GetEnvironmentVariable("BUTCHI_E2E_APP");
        if (string.IsNullOrWhiteSpace(appPath))
            return;

        Assert.True(File.Exists(appPath), $"Butchi executable missing: {appPath}");

        var dataDirectory = Path.Combine(Path.GetTempPath(), "butchi-e2e-first-run", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        using var app = Application.Launch(appPath, $"--e2e-first-run \"{dataDirectory}\"");
        using var automation = new UIA3Automation();
        try
        {
            var welcome = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Welcome to Butchi"),
                TimeSpan.FromSeconds(15)).Result;
            Assert.NotNull(welcome);

            var cf = new ConditionFactory(new UIA3PropertyLibrary());
            var download = FindButton(welcome, cf, "WelcomeDownloadModel");
            var finish = FindButton(welcome, cf, "WelcomeFinishSetup");

            Assert.False(finish.IsEnabled);
            download.Invoke();

            var downloaded = Retry.WhileFalse(
                () => string.Equals(
                    welcome.FindFirstDescendant(cf.ByAutomationId("WelcomeStatus"))?.Name,
                    "Model downloaded. Finish setup to start Butchi.",
                    StringComparison.Ordinal),
                TimeSpan.FromSeconds(10)).Success;
            Assert.True(downloaded, "Welcome setup never reached the model-downloaded state.");

            finish = FindButton(welcome, cf, "WelcomeFinishSetup");
            Assert.True(finish.IsEnabled);
            finish.Invoke();

            var settings = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Butchi Settings"),
                TimeSpan.FromSeconds(10)).Result;
            Assert.NotNull(settings);

            Assert.DoesNotContain(
                app.GetAllTopLevelWindows(automation),
                w => w.Title == "Welcome to Butchi");
        }
        finally
        {
            if (!app.HasExited)
                app.Kill();

            try
            {
                if (Directory.Exists(dataDirectory))
                    Directory.Delete(dataDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static Button FindButton(
        FlaUI.Core.AutomationElements.Window window,
        ConditionFactory cf,
        string automationId)
    {
        var button = Retry.WhileNull(
            () => window.FindFirstDescendant(cf.ByAutomationId(automationId))?.AsButton(),
            TimeSpan.FromSeconds(5)).Result;
        Assert.NotNull(button);
        return button;
    }
}
