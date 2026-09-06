using Xunit;

namespace Butchi.App.Tests;

public sealed class WelcomeSetupDownloadUiContractTests
{
    [Fact]
    public void Welcome_setup_surfaces_separate_download_and_finish_controls()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Butchi.App",
            "Startup",
            "WelcomeSetupViewModel.cs"));
        var view = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Butchi.App",
            "Startup",
            "WelcomeSetupWindow.cs"));

        Assert.Contains("DownloadActionText", viewModel, StringComparison.Ordinal);
        Assert.Contains("DownloadSelectedModelAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("Retry download", viewModel, StringComparison.Ordinal);
        Assert.Contains("DownloadProgressText", viewModel, StringComparison.Ordinal);

        Assert.Contains("_download = new Button", view, StringComparison.Ordinal);
        Assert.Contains("DownloadModelAsync()", view, StringComparison.Ordinal);
        Assert.Contains("_progressText", view, StringComparison.Ordinal);
        Assert.Contains("_progress.IsIndeterminate", view, StringComparison.Ordinal);
        Assert.Contains("_theme.IsEnabled = !isBusy", view, StringComparison.Ordinal);
        Assert.Contains("_targetLanguage.IsEnabled = !isBusy", view, StringComparison.Ordinal);
        Assert.Contains("_resultAction.IsEnabled = !isBusy", view, StringComparison.Ordinal);
        Assert.Contains("_model.IsEnabled = !isBusy", view, StringComparison.Ordinal);
        Assert.Contains("CancelActiveOperation()", view, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not locate Butchi repository root.");
    }
}
