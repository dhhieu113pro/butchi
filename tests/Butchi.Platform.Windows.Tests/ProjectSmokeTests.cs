using Xunit;

namespace Butchi.Platform.Windows.Tests;

public sealed class ProjectSmokeTests
{
    [Fact]
    public void Assembly_is_loadable() => Assert.Equal("Butchi.Platform.Windows", typeof(Butchi.Platform.Windows.AssemblyMarker).Assembly.GetName().Name);
}
