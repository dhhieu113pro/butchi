using Butchi.App.Management;
using Butchi.App.Screenshots;
using Butchi.Core.Configuration;

namespace Butchi.App.Startup;

public sealed class ScreenshotStartup(ButchiRuntimeFactory runtimeFactory)
{
    public async Task<bool> TryRunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (Array.IndexOf(args, "--e2e") >= 0)
        {
            var request = new ScreenshotRequest(
                Path.Combine(Path.GetTempPath(), "butchi-e2e-unused.png"),
                ManagementPage.General,
                1120,
                760,
                "empty",
                AppThemePreference.System);
            var window = await runtimeFactory.CreateManagementScreenshotAsync(request, cancellationToken);
            window.Show(ManagementPage.General);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        }

        var hasManagementScreenshot = ScreenshotRequest.TryParse(args, out var request);
        var popoverIndex = Array.IndexOf(args, "--screenshot-popover");
        if (!hasManagementScreenshot && popoverIndex < 0) return false;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (request is not null)
        {
            var window = await runtimeFactory.CreateManagementScreenshotAsync(request, cancellationToken);
            ScreenshotRunner.Run(request, window, () => { window.Hide(); completion.TrySetResult(); });
        }
        else
        {
            var outputPath = RequireOptionValue(args, popoverIndex, "--screenshot-popover");
            var fixture = GetOptionValue(args, "--fixture") ?? "success";
            var theme = ScreenshotRequest.ParseTheme(GetOptionValue(args, "--theme") ?? "system");
            var window = runtimeFactory.CreatePopoverScreenshot(fixture, theme);
            ScreenshotRunner.RunPopover(outputPath, window, () => { window.Destroy(); completion.TrySetResult(); });
        }

        await completion.Task.WaitAsync(cancellationToken);
        return true;
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string RequireOptionValue(string[] args, int optionIndex, string option)
    {
        if (optionIndex < 0 || optionIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[optionIndex + 1]))
            throw new ArgumentException($"{option} requires an output path.", nameof(args));
        return args[optionIndex + 1];
    }
}
