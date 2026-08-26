using Butchi.App.Management;

namespace Butchi.App.Screenshots;

public sealed record ScreenshotRequest(
    string OutputPath,
    ManagementPage Page,
    int Width,
    int Height)
{
    public static bool TryParse(string[] args, out ScreenshotRequest? request)
    {
        ArgumentNullException.ThrowIfNull(args);
        request = null;

        var screenshotIndex = Array.IndexOf(args, "--screenshot");
        if (screenshotIndex < 0)
            return false;

        if (screenshotIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[screenshotIndex + 1]))
            throw new ArgumentException("--screenshot requires an output path.", nameof(args));

        var outputPath = args[screenshotIndex + 1];
        var page = ManagementPage.Settings;
        var width = 1280;
        var height = 800;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--page":
                    page = ParsePage(RequiredValue(args, ref i, "--page"));
                    break;
                case "--width":
                    width = ParsePositiveInt(RequiredValue(args, ref i, "--width"), "--width");
                    break;
                case "--height":
                    height = ParsePositiveInt(RequiredValue(args, ref i, "--height"), "--height");
                    break;
            }
        }

        request = new ScreenshotRequest(outputPath, page, width, height);
        return true;
    }

    private static string RequiredValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.", nameof(args));
        return args[index];
    }

    private static ManagementPage ParsePage(string value) => value.ToLowerInvariant() switch
    {
        "settings" => ManagementPage.Settings,
        "history" => ManagementPage.History,
        "models" => ManagementPage.Models,
        "status" => ManagementPage.Status,
        _ => throw new ArgumentException($"Unknown management page '{value}'.", nameof(value))
    };

    private static int ParsePositiveInt(string value, string option) =>
        int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"{option} must be a positive integer.", nameof(value));
}
