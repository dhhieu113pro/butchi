using System.Text.Json;
using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.Inference;
using Butchi.Infrastructure;

namespace Butchi.App.Diagnostics;

public static class ReleaseProbe
{
    public static bool TryParse(string[] args, out string? outputPath)
    {
        outputPath = null;
        var index = Array.IndexOf(args, "--release-probe");
        if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            return false;

        outputPath = args[index + 1];
        return true;
    }

    public static async Task<int> RunAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        ReleaseProbeResult result;
        try
        {
            var paths = new AppPaths();
            paths.EnsureDirectories();
            var configStore = new JsonAppConfigStoreAdapter(new JsonConfigStore(paths));
            _ = await GeneralSettingsViewModel.CreateAsync(configStore, cancellationToken);
            _ = await PromptsViewModel.CreateAsync(configStore, cancellationToken);

            using var httpClient = new HttpClient();
            var downloader = new ModelDownloader(new HuggingFaceModelDownloadSource(httpClient));
            await using var inferenceEngine = new LLamaSharpInferenceEngine(
                new LLamaSharpRuntimeFactory(request => paths.ModelPath(request.ModelRepo, request.ModelFile)));
            var modelManager = new FileModelManager(paths, downloader, inferenceEngine, configStore);
            _ = await ModelManagementViewModel.CreateAsync(modelManager, configStore, cancellationToken);

            var identity = Environment.GetEnvironmentVariable("BUTCHI_RELEASE_PROBE_PACKAGE_IDENTITY") ?? "Butchi";
            var version = Environment.GetEnvironmentVariable("BUTCHI_RELEASE_PROBE_PACKAGE_VERSION")
                ?? typeof(ReleaseProbe).Assembly.GetName().Version?.ToString(4)
                ?? "0.0.0.0";
            result = ReleaseProbeResult.CreateSuccess(identity, version);
        }
        catch (Exception ex)
        {
            result = ReleaseProbeResult.Failure(ex.GetType().Name);
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            cancellationToken);
        return result.Success ? 0 : 1;
    }
}

public sealed record ReleaseProbeResult(
    bool Success,
    bool CompositionHealthy,
    string PackageIdentity,
    string PackageVersion,
    string? ErrorCode,
    string? SelectedText,
    string? PromptContent,
    string? HistoryContent)
{
    public static ReleaseProbeResult CreateSuccess(string packageIdentity, string packageVersion) =>
        new(true, true, packageIdentity, packageVersion, null, null, null, null);

    public static ReleaseProbeResult Failure(string errorCode) =>
        new(false, false, string.Empty, string.Empty, errorCode, null, null, null);
}
