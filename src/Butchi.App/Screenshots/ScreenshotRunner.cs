using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Butchi.App.Management;
using Butchi.App.Popover;

namespace Butchi.App.Screenshots;

public static class ScreenshotRunner
{
    public static void Run(
        ScreenshotRequest request,
        ManagementWindow window,
        Action completed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(completed);

        window.Width = request.Width;
        window.Height = request.Height;
        window.CanResize = false;

        window.Opened += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                RenderManagementContent(window, request.OutputPath, request.Width, request.Height);
                completed();
            }, DispatcherPriority.Loaded);
        };

        window.Show(request.Page);
    }

    public static void RunPopover(
        string outputPath,
        PopoverWindow window,
        Action completed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(completed);

        window.Opened += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var width = Math.Max(1, (int)Math.Ceiling(window.Bounds.Width));
                var height = Math.Max(1, (int)Math.Ceiling(window.Bounds.Height));
                Render(window, outputPath, width, height);
                completed();
            }, DispatcherPriority.Loaded);
        };

        window.ShowPersistent();
    }

    private static void RenderManagementContent(Window window, string outputPath, int width, int height)
    {
        if (window.Content is not Control content)
            throw new InvalidOperationException("Management window content must be an Avalonia control.");

        var fullOutputPath = PrepareOutputPath(outputPath);

        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));

        using var bitmap = new RenderTargetBitmap(
            new PixelSize(width, height),
            new Vector(96, 96));
        bitmap.Render(content);
        bitmap.Save(fullOutputPath, PngBitmapEncoderOptions.Default);
    }

    private static void Render(Window window, string outputPath, int width, int height)
    {
        var fullOutputPath = PrepareOutputPath(outputPath);

        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));

        using var bitmap = new RenderTargetBitmap(
            new PixelSize(width, height),
            new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(fullOutputPath, PngBitmapEncoderOptions.Default);
    }

    private static string PrepareOutputPath(string outputPath)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        return fullOutputPath;
    }
}
