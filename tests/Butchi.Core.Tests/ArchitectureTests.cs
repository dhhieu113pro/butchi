using System.Xml.Linq;
using Xunit;

namespace Butchi.Core.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Core_must_not_reference_ui_inference_or_windows_dependencies()
    {
        var assemblyReferences = typeof(Butchi.Core.Actions.TextAction).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(assemblyReferences, name => name.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain(assemblyReferences, name => name.StartsWith("LLamaSharp", StringComparison.Ordinal));
        Assert.DoesNotContain("Butchi.Platform.Windows", assemblyReferences);

        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var projectPath = Path.Combine(repositoryRoot, "src", "Butchi.Core", "Butchi.Core.csproj");

        var project = XDocument.Load(projectPath);
        var includes = project
            .Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(includes, include =>
            include.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(includes, include =>
            include.StartsWith("LLamaSharp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(includes, include =>
            include.Contains("Butchi.Platform.Windows", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot(string startPath)
    {
        for (var directory = new DirectoryInfo(startPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Butchi.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"Repository root containing Butchi.slnx was not found from {startPath}");
    }
}
