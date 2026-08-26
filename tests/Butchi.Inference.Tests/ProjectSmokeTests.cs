using Xunit;

namespace Butchi.Inference.Tests;

public sealed class ProjectSmokeTests
{
    [Fact]
    public void Assembly_is_loadable() => Assert.Equal("Butchi.Inference", typeof(Butchi.Inference.AssemblyMarker).Assembly.GetName().Name);
}
