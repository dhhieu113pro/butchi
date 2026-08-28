using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Butchi.Platform.Windows.Selection;

public sealed class WindowsUiAutomationSelectionSource : IUiAutomationSelectionSource
{
    public ValueTask<string?> TryGetSelectedTextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return ValueTask.FromResult<string?>(null);

        return ValueTask.FromResult(TryGetSelectedText());
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetSelectedText()
    {
        object? automation = null;
        object? focused = null;
        object? pattern = null;
        object? ranges = null;
        object? range = null;
        try
        {
            var automationType = Type.GetTypeFromProgID("UIAutomationClient.CUIAutomation");
            if (automationType is null)
                return null;

            automation = Activator.CreateInstance(automationType);
            if (automation is null)
                return null;

            focused = ((dynamic)automation).GetFocusedElement();
            if (focused is null)
                return null;

            const int textPatternId = 10014;
            pattern = ((dynamic)focused).GetCurrentPattern(textPatternId);
            if (pattern is null)
                return null;

            ranges = ((dynamic)pattern).GetSelection();
            if (ranges is null || (int)((dynamic)ranges).Length <= 0)
                return null;

            range = ((dynamic)ranges).GetElement(0);
            var text = (string?)((dynamic)range).GetText(-1);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            Release(range);
            Release(ranges);
            Release(pattern);
            Release(focused);
            Release(automation);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
