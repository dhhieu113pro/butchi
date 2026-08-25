using Xunit;

namespace Butchi.Core.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Core_must_not_reference_ui_inference_or_windows_projects()
    {
        var references = typeof(Butchi.Core.Actions.TextAction).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .ToArray();

        Assert.DoesNotContain("Avalonia", references);
        Assert.DoesNotContain("LLamaSharp", references);
        Assert.DoesNotContain("Butchi.Platform.Windows", references);
    }
}
