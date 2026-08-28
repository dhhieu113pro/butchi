namespace Butchi.App.Tests;

public sealed class WindowsInteractionRuntimeContractTests
{
    [Fact]
    public void Runtime_factory_wires_and_starts_windows_selection_activation()
    {
        var root = FindRepositoryRoot();
        var factory = File.ReadAllText(Path.Combine(root, "src", "Butchi.App", "Startup", "ButchiRuntimeFactory.cs"));
        var runtime = File.ReadAllText(Path.Combine(root, "src", "Butchi.App", "Startup", "ButchiRuntime.cs"));

        Assert.Contains("WindowsTriggerService", factory, StringComparison.Ordinal);
        Assert.Contains("WindowsActivationCoordinator", factory, StringComparison.Ordinal);
        Assert.Contains("WindowsInteractionRuntime", factory, StringComparison.Ordinal);
        Assert.Contains("interaction.Start()", runtime, StringComparison.Ordinal);
        Assert.Contains("interaction.Dispose()", runtime, StringComparison.Ordinal);
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
