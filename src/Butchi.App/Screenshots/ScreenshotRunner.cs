using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Butchi.App.Management;

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
                var outputPath = Path.GetFullPath(request.OutputPath);
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                window.Measure(new Size(request.Width, request.Height));
                window.Arrange(new Rect(0, 0, request.Width, request.Height));

                using var bitmap = new RenderTargetBitmap(
                    new PixelSize(request.Width, request.Height),
                    new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(outputPath, PngBitmapEncoderOptions.Default);
                completed();
            }, DispatcherPriority.Loaded);
        };

        window.Show(request.Page);
    }
}
