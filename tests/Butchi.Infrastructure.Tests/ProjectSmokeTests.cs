using Xunit;

namespace Butchi.Infrastructure.Tests;

public sealed class ProjectSmokeTests
{
    [Fact]
    public void Assembly_is_loadable() => Assert.Equal("Butchi.Infrastructure", typeof(Butchi.Infrastructure.AssemblyMarker).Assembly.GetName().Name);
}
