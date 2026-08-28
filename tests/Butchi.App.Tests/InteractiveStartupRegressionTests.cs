using Xunit;

namespace Butchi.App.Tests;

public sealed class InteractiveStartupRegressionTests
{
    [Fact]
    public void Interactive_startup_sources_do_not_block_async_work()
    {
        var root = FindRepositoryRoot();
        var sources = new[] { Path.Combine(root, "src", "Butchi.App", "App.cs") }
            .Concat(Directory.EnumerateFiles(
                Path.Combine(root, "src", "Butchi.App", "Startup"),
                "*.cs"));

        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            Assert.DoesNotContain("GetAwaiter().GetResult()", text, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\.Wait\s*\(", text);
            Assert.DoesNotMatch(@"\.Result\b", text);
        }
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
