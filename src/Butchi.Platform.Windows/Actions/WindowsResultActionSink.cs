using Butchi.Core.Actions;
using Butchi.Platform.Windows.Selection;

namespace Butchi.Platform.Windows.Actions;

public sealed class WindowsResultActionSink(
    IClipboardSelectionSource clipboard,
    IWindowsPasteSender pasteSender,
    TimeSpan pasteConsumptionDelay) : IResultActionSink
{
    public async Task CopyAsync(string text, CancellationToken cancellationToken)
    {
        await clipboard.SetClipboardTextAsync(text, cancellationToken);
    }

    public async Task ReplaceAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = await clipboard.GetClipboardTextAsync(cancellationToken);
        var mutated = false;

        try
        {
            await clipboard.SetClipboardTextAsync(text, cancellationToken);
            mutated = true;
            cancellationToken.ThrowIfCancellationRequested();
            await pasteSender.SendPasteAsync(cancellationToken);
            if (pasteConsumptionDelay > TimeSpan.Zero)
                await Task.Delay(pasteConsumptionDelay, CancellationToken.None);
        }
        finally
        {
            if (mutated)
                await clipboard.SetClipboardTextAsync(previous, CancellationToken.None);
        }
    }
}
