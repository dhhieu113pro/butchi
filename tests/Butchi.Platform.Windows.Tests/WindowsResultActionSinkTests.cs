using Butchi.Platform.Windows.Actions;
using Butchi.Platform.Windows.Selection;
using Xunit;

namespace Butchi.Platform.Windows.Tests;

public sealed class WindowsResultActionSinkTests
{
    [Fact]
    public async Task Copy_writes_generated_text_to_clipboard()
    {
        var clipboard = new FakeClipboard("before");
        var paste = new FakePasteSender();
        var sink = new WindowsResultActionSink(clipboard, paste, TimeSpan.Zero);

        await sink.CopyAsync("rewritten", CancellationToken.None);

        Assert.Equal("rewritten", clipboard.CurrentText);
        Assert.Equal(new[] { "set:rewritten" }, clipboard.Events);
        Assert.Empty(paste.Events);
    }

    [Fact]
    public async Task Replace_pastes_generated_text_then_restores_original_clipboard()
    {
        var clipboard = new FakeClipboard("before");
        var paste = new FakePasteSender();
        var sink = new WindowsResultActionSink(clipboard, paste, TimeSpan.Zero);

        await sink.ReplaceAsync("rewritten", CancellationToken.None);

        Assert.Equal("before", clipboard.CurrentText);
        Assert.Equal(new[] { "get", "set:rewritten", "set:before" }, clipboard.Events);
        Assert.Equal(new[] { "paste" }, paste.Events);
    }

    [Fact]
    public async Task Replace_clears_clipboard_when_it_was_previously_empty()
    {
        var clipboard = new FakeClipboard(null);
        var paste = new FakePasteSender();
        var sink = new WindowsResultActionSink(clipboard, paste, TimeSpan.Zero);

        await sink.ReplaceAsync("rewritten", CancellationToken.None);

        Assert.Null(clipboard.CurrentText);
        Assert.Equal(new[] { "get", "set:rewritten", "set:<null>" }, clipboard.Events);
    }

    [Fact]
    public async Task Replace_restores_original_clipboard_when_paste_fails()
    {
        var clipboard = new FakeClipboard("before");
        var paste = new FakePasteSender { ThrowOnPaste = true };
        var sink = new WindowsResultActionSink(clipboard, paste, TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sink.ReplaceAsync("rewritten", CancellationToken.None));

        Assert.Equal("before", clipboard.CurrentText);
        Assert.Equal(new[] { "get", "set:rewritten", "set:before" }, clipboard.Events);
    }

    private sealed class FakeClipboard(string? initial) : IClipboardSelectionSource
    {
        public string? CurrentText { get; private set; } = initial;
        public List<string> Events { get; } = [];

        public ValueTask<string?> CaptureAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(CurrentText);

        public ValueTask<string?> GetClipboardTextAsync(CancellationToken cancellationToken)
        {
            Events.Add("get");
            return ValueTask.FromResult(CurrentText);
        }

        public ValueTask SetClipboardTextAsync(string? text, CancellationToken cancellationToken)
        {
            Events.Add($"set:{text ?? "<null>"}");
            CurrentText = text;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakePasteSender : IWindowsPasteSender
    {
        public bool ThrowOnPaste { get; init; }
        public List<string> Events { get; } = [];

        public Task SendPasteAsync(CancellationToken cancellationToken)
        {
            Events.Add("paste");
            if (ThrowOnPaste)
                throw new InvalidOperationException("paste failed");
            return Task.CompletedTask;
        }
    }
}
