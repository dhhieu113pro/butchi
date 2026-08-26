namespace Butchi.Platform.Windows.Selection;

public interface IUiAutomationSelectionSource
{
    ValueTask<string?> TryGetSelectedTextAsync(CancellationToken cancellationToken);
}

public interface IClipboardSelectionSource
{
    ValueTask<string?> CaptureAsync(CancellationToken cancellationToken);
    ValueTask<string?> GetClipboardTextAsync(CancellationToken cancellationToken);
    ValueTask SetClipboardTextAsync(string? text, CancellationToken cancellationToken);
}

public interface IWindowsSelectionReader
{
    ValueTask<string?> ReadSelectedTextAsync(CancellationToken cancellationToken);
}

public sealed class WindowsSelectionReader(
    IUiAutomationSelectionSource uiAutomation,
    IClipboardSelectionSource clipboard) : IWindowsSelectionReader
{
    public async ValueTask<string?> ReadSelectedTextAsync(CancellationToken cancellationToken)
    {
        var selected = await uiAutomation.TryGetSelectedTextAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(selected))
            return selected;

        var original = await clipboard.GetClipboardTextAsync(cancellationToken);
        try
        {
            return await clipboard.CaptureAsync(cancellationToken);
        }
        finally
        {
            await clipboard.SetClipboardTextAsync(original, CancellationToken.None);
        }
    }
}
