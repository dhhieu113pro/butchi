using Xunit;

namespace Butchi.App.Tests;

public sealed class Task14GeneralUiContractTests
{
    [Fact]
    public void Task14_1_defines_five_section_shell_and_general_autosave_surface()
    {
        var root = FindRepositoryRoot();
        var shellPath = Path.Combine(root, "src", "Butchi.App", "Management", "ManagementShellViewModel.cs");
        var generalVmPath = Path.Combine(root, "src", "Butchi.App", "Settings", "GeneralSettingsViewModel.cs");
        var generalViewPath = Path.Combine(root, "src", "Butchi.App", "Settings", "GeneralSettingsView.cs");
        var themePath = Path.Combine(root, "src", "Butchi.App", "Styling", "ButchiTheme.cs");

        var shell = File.ReadAllText(shellPath);
        Assert.Contains("General", shell, StringComparison.Ordinal);
        Assert.Contains("Prompts", shell, StringComparison.Ordinal);
        Assert.Contains("Model", shell, StringComparison.Ordinal);
        Assert.Contains("History", shell, StringComparison.Ordinal);
        Assert.Contains("AboutPrivacy", shell, StringComparison.Ordinal);

        Assert.True(File.Exists(generalVmPath), $"Missing Task 14.1 General view model: {generalVmPath}");
        Assert.True(File.Exists(generalViewPath), $"Missing Task 14.1 General view: {generalViewPath}");
        Assert.True(File.Exists(themePath), $"Missing Task 14.1 shared visual system: {themePath}");

        var generalVm = File.ReadAllText(generalVmPath);
        Assert.Contains("SaveStatus", generalVm, StringComparison.Ordinal);
        Assert.Contains("SetThemeAsync", generalVm, StringComparison.Ordinal);
        Assert.Contains("SetLaunchAtLoginAsync", generalVm, StringComparison.Ordinal);
        Assert.Contains("SetTranslateEnabledAsync", generalVm, StringComparison.Ordinal);
        Assert.Contains("SetRewriteEnabledAsync", generalVm, StringComparison.Ordinal);
        Assert.Contains("SetTargetLanguageAsync", generalVm, StringComparison.Ordinal);
        Assert.Contains("SetFavoriteLanguagesAsync", generalVm, StringComparison.Ordinal);
        Assert.Contains("SetResultActionAsync", generalVm, StringComparison.Ordinal);
        Assert.Contains("SetPopoverHideSecondsAsync", generalVm, StringComparison.Ordinal);
    }

    [Fact]
    public void General_view_exposes_launch_at_login_toggle_and_syncs_rollbacks()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Butchi.App",
            "Settings",
            "GeneralSettingsView.cs"));

        Assert.Contains("Launch Butchi at login", view, StringComparison.Ordinal);
        Assert.Contains("Start Butchi automatically when you sign in.", view, StringComparison.Ordinal);
        Assert.Contains("SetLaunchAtLoginAsync", view, StringComparison.Ordinal);
        Assert.Contains("nameof(GeneralSettingsViewModel.LaunchAtLogin)", view, StringComparison.Ordinal);
        Assert.Contains("_launchAtLogin.IsChecked = _viewModel.LaunchAtLogin", view, StringComparison.Ordinal);
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
