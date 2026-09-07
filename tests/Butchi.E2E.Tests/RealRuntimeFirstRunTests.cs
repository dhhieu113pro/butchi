using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace Butchi.E2E.Tests;

public sealed class RealRuntimeFirstRunTests
{
    [Fact]
    public void First_run_starts_real_runtime_and_survives_popover_lifecycle()
    {
        var appPath = Environment.GetEnvironmentVariable("BUTCHI_E2E_APP");
        if (string.IsNullOrWhiteSpace(appPath))
            return;

        Assert.True(File.Exists(appPath), $"Butchi executable missing: {appPath}");
        var dataDirectory = Path.Combine(Path.GetTempPath(), "butchi-e2e-real-runtime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        string Diagnostics()
        {
            var path = Path.Combine(dataDirectory, "startup-diagnostics.txt");
            return File.Exists(path) ? File.ReadAllText(path) : "No runtime diagnostics were written.";
        }

        using var app = Application.Launch(appPath, $"--e2e-first-run-real-runtime \"{dataDirectory}\"");
        using var automation = new UIA3Automation();
        try
        {
            var welcome = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Welcome to Butchi"),
                TimeSpan.FromSeconds(15)).Result;
            Assert.NotNull(welcome);

            var cf = new ConditionFactory(new UIA3PropertyLibrary());
            Assert.NotNull(welcome.FindFirstDescendant(cf.ByAutomationId("WelcomeModel")));
            var download = FindButton(welcome, cf, "WelcomeDownloadModel");
            var finish = FindButton(welcome, cf, "WelcomeFinishSetup");
            Assert.False(finish.IsEnabled);
            Assert.True(download.IsEnabled, "The deterministic model must be available for download.");
            download.Invoke();
            Assert.True(Retry.WhileFalse(
                () => string.Equals(
                    welcome.FindFirstDescendant(cf.ByAutomationId("WelcomeStatus"))?.Name,
                    "Model downloaded. Finish setup to start Butchi.",
                    StringComparison.Ordinal),
                TimeSpan.FromSeconds(10)).Success,
                "The deterministic model did not download. " + ReadWelcomeError(welcome, cf));
            FindButton(welcome, cf, "WelcomeFinishSetup").Invoke();

            // Unlike the original first-run test, this must construct the production
            // management, popover, tray and interaction runtime, not a placeholder window.
            var settings = Retry.WhileNull(
                () => app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Butchi Settings"),
                TimeSpan.FromSeconds(15)).Result;
            Assert.True(settings is not null, "Real desktop runtime did not start. " + Diagnostics());
            Assert.True(Retry.WhileFalse(
                () => app.GetAllTopLevelWindows(automation).All(w => w.Title != "Welcome to Butchi"),
                TimeSpan.FromSeconds(5)).Success,
                "Welcome did not close after runtime startup. " + Diagnostics());

            var completed = Retry.WhileFalse(
                () => app.GetAllTopLevelWindows(automation).Any(w =>
                    w.Title is "Butchi Popover Lifecycle Complete" or "Butchi Popover Lifecycle Failed"),
                TimeSpan.FromSeconds(15)).Success;
            Assert.True(completed, "Real popover lifecycle did not complete. " + Diagnostics());
            var result = app.GetAllTopLevelWindows(automation).First(w =>
                w.Title is "Butchi Popover Lifecycle Complete" or "Butchi Popover Lifecycle Failed");
            Assert.True(result.Title == "Butchi Popover Lifecycle Complete", Diagnostics());
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
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string ReadWelcomeError(FlaUI.Core.AutomationElements.Window welcome, ConditionFactory cf)
    {
        try
        {
            return welcome.FindFirstDescendant(cf.ByAutomationId("WelcomeError"))?.Name ?? string.Empty;
        }
        catch (FlaUI.Core.Exceptions.ElementNotAvailableException)
        {
            return string.Empty;
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
