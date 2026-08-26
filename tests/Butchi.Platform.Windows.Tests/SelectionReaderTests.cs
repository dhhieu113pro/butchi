using Butchi.Platform.Windows.Selection;
using Xunit;

namespace Butchi.Platform.Windows.Tests;

public sealed class SelectionReaderTests
{
    [Fact]
    public async Task Uses_ui_automation_selection_before_clipboard_fallback()
    {
        var uia = new FakeUia("selected by uia");
        var clipboard = new FakeClipboard("original", "fallback");
        var reader = new WindowsSelectionReader(uia, clipboard);

        var result = await reader.ReadSelectedTextAsync(CancellationToken.None);

        Assert.Equal("selected by uia", result);
        Assert.Equal(0, clipboard.CopyAttempts);
    }

    [Fact]
    public async Task Falls_back_to_ctrl_c_and_restores_previous_clipboard()
    {
        var uia = new FakeUia(null);
        var clipboard = new FakeClipboard("original", "copied selection");
        var reader = new WindowsSelectionReader(uia, clipboard);

        var result = await reader.ReadSelectedTextAsync(CancellationToken.None);

        Assert.Equal("copied selection", result);
        Assert.Equal("original", clipboard.CurrentText);
        Assert.Equal(1, clipboard.CopyAttempts);
    }

    [Fact]
    public async Task Clipboard_is_restored_even_when_copy_fallback_fails()
    {
        var uia = new FakeUia(null);
        var clipboard = new FakeClipboard("original", null, throwOnCopy: true);
        var reader = new WindowsSelectionReader(uia, clipboard);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.ReadSelectedTextAsync(CancellationToken.None).AsTask());

        Assert.Equal("original", clipboard.CurrentText);
    }

    private sealed class FakeUia(string? text) : IUiAutomationSelectionSource
    {
        public ValueTask<string?> TryGetSelectedTextAsync(CancellationToken cancellationToken) => ValueTask.FromResult(text);
    }

    private sealed class FakeClipboard(string? original, string? copied, bool throwOnCopy = false) : IClipboardSelectionSource
    {
        public string? CurrentText { get; private set; } = original;
        public int CopyAttempts { get; private set; }

        public ValueTask<string?> CaptureAsync(CancellationToken cancellationToken)
        {
            CopyAttempts++;
            if (throwOnCopy)
                throw new InvalidOperationException("copy failed");
            CurrentText = copied;
            return ValueTask.FromResult(copied);
        }

        public ValueTask<string?> GetClipboardTextAsync(CancellationToken cancellationToken) => ValueTask.FromResult(CurrentText);

        public ValueTask SetClipboardTextAsync(string? text, CancellationToken cancellationToken)
        {
            CurrentText = text;
            return ValueTask.CompletedTask;
        }
    }
}
