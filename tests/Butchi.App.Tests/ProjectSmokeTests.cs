using Xunit;

namespace Butchi.App.Tests;

public sealed class ProjectSmokeTests
{
    [Fact]
    public void App_is_Avalonia_application() => Assert.True(typeof(Avalonia.Application).IsAssignableFrom(typeof(Butchi.App.App)));
}
