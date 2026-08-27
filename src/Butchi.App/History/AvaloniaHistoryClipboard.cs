using Avalonia.Input.Platform;

namespace Butchi.App.History;

public sealed class AvaloniaHistoryClipboard(Func<IClipboard?> resolveClipboard) : IHistoryClipboard
{
    public async ValueTask SetTextAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = resolveClipboard() ?? throw new InvalidOperationException("Clipboard is not available.");
        await clipboard.SetTextAsync(text);
    }
}
