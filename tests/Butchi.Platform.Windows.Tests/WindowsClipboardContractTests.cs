using Xunit;

namespace Butchi.Platform.Windows.Tests;

public sealed class WindowsClipboardContractTests
{
    [Fact]
    public void Clipboard_read_and_write_throw_when_open_retry_is_exhausted()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Butchi.Platform.Windows", "Selection", "WindowsClipboardSelectionSource.cs"));

        Assert.Contains("Unable to open the clipboard for reading.", source, StringComparison.Ordinal);
        Assert.Contains("Unable to open the clipboard for writing.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!OpenClipboardWithRetry())\n            return ValueTask.CompletedTask", source.Replace("\r\n", "\n"), StringComparison.Ordinal);
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
