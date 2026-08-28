using System.Reflection;
using Butchi.App.Diagnostics;
using Xunit;

namespace Butchi.App.Tests;

public sealed class PackagedBehaviorProbeTests
{
    [Fact]
    public async Task Startup_behavior_probe_reaches_tray_through_first_run_setup()
    {
        var result = await StartupBehaviorProbe.RunAsync(CancellationToken.None);

        Assert.True(result.FirstRunCompositionReady);
        Assert.True(result.TrayReady);
    }

    [Fact]
    public void Program_entry_point_is_async()
    {
        var program = typeof(App).Assembly.GetType("Butchi.App.Program", throwOnError: true)!;
        var main = program.GetMethod("Main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        Assert.Equal(typeof(Task<int>), main.ReturnType);
    }

    [Fact]
    public void Release_probe_exposes_packaged_behavior_readiness_without_user_content()
    {
        var properties = typeof(ReleaseProbeResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("FirstRunCompositionReady", properties);
        Assert.Contains("TrayReady", properties);
        Assert.Contains("SettingsReady", properties);
        Assert.Contains("ModelsReady", properties);
        Assert.Contains("HistoryReady", properties);

        var result = ReleaseProbeResult.CreateSuccess("Butchi.Test", "0.1.0.0");
        Assert.Null(result.SelectedText);
        Assert.Null(result.PromptContent);
        Assert.Null(result.HistoryContent);
    }

    [Fact]
    public void Installed_msix_smoke_requires_all_packaged_behavior_readiness_flags()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "Release", "Test-InstalledMsix.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("firstRunCompositionReady", script, StringComparison.Ordinal);
        Assert.Contains("trayReady", script, StringComparison.Ordinal);
        Assert.Contains("settingsReady", script, StringComparison.Ordinal);
        Assert.Contains("modelsReady", script, StringComparison.Ordinal);
        Assert.Contains("historyReady", script, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Butchi.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Butchi repository root from the test output directory.");
    }
}
