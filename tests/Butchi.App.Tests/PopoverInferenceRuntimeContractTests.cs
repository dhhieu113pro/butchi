using Xunit;

namespace Butchi.App.Tests;

public sealed class PopoverInferenceRuntimeContractTests
{
    [Fact]
    public void Production_runtime_composes_popover_inference_pipeline()
    {
        var root = FindRepositoryRoot();
        var factory = File.ReadAllText(Path.Combine(root, "src", "Butchi.App", "Startup", "ButchiRuntimeFactory.cs"));

        Assert.Contains("new WindowsResultActionSink(", factory, StringComparison.Ordinal);
        Assert.Contains("new WindowsPasteSender()", factory, StringComparison.Ordinal);
        Assert.Contains("new TextActionScheduler(services.InferenceEngine", factory, StringComparison.Ordinal);
        Assert.Contains("new PopoverActionController(", factory, StringComparison.Ordinal);
        Assert.Contains("popoverViewModel.SetSession(string.Empty, TextAction.Translate, config.TargetLanguage)", factory, StringComparison.Ordinal);
        Assert.Contains("popoverActionController, scheduler", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_disposes_popover_controller_and_scheduler()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(root, "src", "Butchi.App", "Startup", "ButchiRuntime.cs"));

        Assert.Contains("PopoverActionController popoverActionController", runtime, StringComparison.Ordinal);
        Assert.Contains("TextActionScheduler scheduler", runtime, StringComparison.Ordinal);
        Assert.Contains("await popoverActionController.DisposeAsync()", runtime, StringComparison.Ordinal);
        Assert.Contains("await scheduler.DisposeAsync()", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Popover_screenshot_factory_stays_inference_free()
    {
        var root = FindRepositoryRoot();
        var factory = File.ReadAllText(Path.Combine(root, "src", "Butchi.App", "Startup", "ButchiRuntimeFactory.cs"));
        var start = factory.IndexOf("public PopoverWindow CreatePopoverScreenshot", StringComparison.Ordinal);
        var end = factory.IndexOf("private async ValueTask<ManagementWindow>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var screenshotBlock = factory[start..end];

        Assert.DoesNotContain("TextActionScheduler", screenshotBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("PopoverActionController", screenshotBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowsResultActionSink", screenshotBlock, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
