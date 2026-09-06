using Butchi.App.Vision;
using Xunit;

namespace Butchi.App.Tests;

public sealed class VisionViewModelTests
{
    [Fact]
    public void Capture_activates_vision_clears_error_and_raises_request()
    {
        var vm = new VisionViewModel();
        var raised = 0;
        vm.CaptureRequested += (_, _) => raised++;
        vm.Fail("old error");

        vm.RequestCapture();

        Assert.True(vm.IsActive);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(1, raised);

        vm.Deactivate();
        Assert.False(vm.IsActive);
    }

    [Fact]
    public void Screenshot_rejects_missing_data_and_resets_previous_run_state()
    {
        var vm = new VisionViewModel();
        Assert.Throws<ArgumentNullException>(() => vm.SetScreenshot(null!));
        Assert.Throws<ArgumentException>(() => vm.SetScreenshot([]));

        vm.SetScreenshot([1, 2, 3]);
        vm.RequestAnalyze();
        vm.Append("old output");
        vm.Fail("old failure");

        var screenshot = new byte[] { 9, 8, 7 };
        vm.SetScreenshot(screenshot);

        Assert.True(vm.IsActive);
        Assert.True(vm.HasScreenshot);
        Assert.Same(screenshot, vm.ScreenshotPng);
        Assert.False(vm.IsRunning);
        Assert.Equal(string.Empty, vm.Output);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Analyze_requires_screenshot_and_non_empty_prompt_then_streams_result()
    {
        var vm = new VisionViewModel();
        string? requestedPrompt = null;
        vm.AnalyzeRequested += (_, prompt) => requestedPrompt = prompt;

        vm.RequestAnalyze();
        Assert.False(vm.IsRunning);
        Assert.Null(requestedPrompt);

        vm.SetScreenshot([1]);
        vm.SetPrompt("   ");
        vm.RequestAnalyze();
        Assert.False(vm.IsRunning);
        Assert.Null(requestedPrompt);

        vm.SetPrompt("  What is here?  ");
        vm.RequestAnalyze();

        Assert.True(vm.IsRunning);
        Assert.Equal("What is here?", requestedPrompt);
        Assert.Equal(string.Empty, vm.Output);
        Assert.Null(vm.ErrorMessage);

        vm.Append(string.Empty);
        vm.Append("A window");
        vm.Append(" and text");
        Assert.Equal("A window and text", vm.Output);

        vm.RequestAnalyze();
        Assert.Equal("What is here?", requestedPrompt);

        vm.Complete();
        Assert.False(vm.IsRunning);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Prompt_update_is_idempotent_and_null_becomes_empty()
    {
        var vm = new VisionViewModel();
        var promptChanges = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VisionViewModel.Prompt))
                promptChanges++;
        };

        var initial = vm.Prompt;
        vm.SetPrompt(initial);
        vm.SetPrompt("custom");
        vm.SetPrompt("custom");
        vm.SetPrompt(null!);

        Assert.Equal(string.Empty, vm.Prompt);
        Assert.Equal(2, promptChanges);
    }

    [Fact]
    public void Fail_uses_custom_or_default_message_and_stops_running()
    {
        var vm = new VisionViewModel();
        vm.SetScreenshot([1]);
        vm.RequestAnalyze();
        Assert.True(vm.IsRunning);

        vm.Fail("camera failed");
        Assert.False(vm.IsRunning);
        Assert.Equal("camera failed", vm.ErrorMessage);

        vm.Fail("  ");
        Assert.Equal("Vision analysis failed.", vm.ErrorMessage);
    }
}
