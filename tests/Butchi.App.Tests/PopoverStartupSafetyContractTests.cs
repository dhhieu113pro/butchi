using Xunit;

namespace Butchi.App.Tests;

public sealed class PopoverStartupSafetyContractTests
{
    [Fact]
    public void Popover_transition_host_is_backed_by_plain_content_control_for_startup_safety()
    {
        var root = FindRepositoryRoot();
        var hostPath = Path.Combine(root, "src", "Butchi.App", "Popover", "SafeTransitionContentControl.cs");
        var source = File.ReadAllText(hostPath);

        Assert.Contains(
            "global using TransitioningContentControl = Butchi.App.Popover.SafeTransitionContentControl;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "internal sealed class SafeTransitionContentControl : ContentControl",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SafeTransitionContentControl : Avalonia.Controls.TransitioningContentControl",
            source,
            StringComparison.Ordinal);
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
