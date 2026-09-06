using Xunit;

namespace Butchi.App.Tests;

public sealed class MarkdownResultRenderingContractTests
{
    [Fact]
    public void Completed_popover_results_render_markdown_instead_of_raw_markers()
    {
        var root = FindRepositoryRoot();
        var packagesPath = Path.Combine(root, "Directory.Packages.props");
        var projectPath = Path.Combine(root, "src", "Butchi.App", "Butchi.App.csproj");
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");

        var packages = File.ReadAllText(packagesPath);
        var project = File.ReadAllText(projectPath);
        var window = File.ReadAllText(windowPath);

        Assert.Contains("Markdown.Avalonia.Tight", packages, StringComparison.Ordinal);
        Assert.Contains("PackageReference Include=\"Markdown.Avalonia.Tight\"", project, StringComparison.Ordinal);
        Assert.Contains("using Markdown.Avalonia;", window, StringComparison.Ordinal);
        Assert.Contains("new MarkdownScrollViewer", window, StringComparison.Ordinal);
        Assert.Contains("Markdown = selected.Output", window, StringComparison.Ordinal);
        Assert.Contains("SelectionEnabled = true", window, StringComparison.Ordinal);
        Assert.Contains("SaveScrollValueWhenContentUpdated = true", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Streaming_results_keep_plain_text_until_completion_so_partial_markdown_is_safe()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var window = File.ReadAllText(windowPath);

        Assert.Contains("selected.IsRunning", window, StringComparison.Ordinal);
        Assert.Contains("selected.Output + \" ▍\"", window, StringComparison.Ordinal);
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
