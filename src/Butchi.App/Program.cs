using Avalonia;
using Butchi.App.Diagnostics;

namespace Butchi.App;

internal static class Program
{
    public static string[] StartupArgs { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        StartupArgs = args;
        if (ReleaseProbe.TryParse(args, out var outputPath))
        {
            Environment.ExitCode = ReleaseProbe.RunAsync(outputPath!).GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
