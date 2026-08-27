using System.Text.Json;
using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.App.Tray;
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
            var dataRoot = Environment.GetEnvironmentVariable("BUTCHI_RELEASE_PROBE_DATA_ROOT");
            var paths = new AppPaths(dataRoot);
            paths.EnsureDirectories();
            var configStore = new JsonAppConfigStoreAdapter(new JsonConfigStore(paths));
            _ = await configStore.LoadAsync(cancellationToken);
            var generalSettings = await GeneralSettingsViewModel.CreateAsync(configStore, cancellationToken);
            var prompts = await PromptsViewModel.CreateAsync(configStore, cancellationToken);
            var settingsReady = generalSettings is not null && prompts is not null;

            var historyStore = new SqliteHistoryStore(paths);
            await historyStore.InitializeAsync(cancellationToken);
            var historyEntries = await historyStore.SearchAsync(limit: 500, cancellationToken: cancellationToken);
            var historyReady = historyEntries is not null;

            using var httpClient = new HttpClient();
            var downloader = new ModelDownloader(new HuggingFaceModelDownloadSource(httpClient));
            await using var inferenceEngine = new LLamaSharpInferenceEngine(
                new LLamaSharpRuntimeFactory(request => paths.ModelPath(request.ModelRepo, request.ModelFile)));
            var modelManager = new FileModelManager(paths, downloader, inferenceEngine, configStore);
            var models = await ModelManagementViewModel.CreateAsync(modelManager, configStore, cancellationToken);
            var modelsReady = models is not null;

            var trayCommands = Enum.GetValues<TrayCommand>();
            var trayReady = trayCommands.Contains(TrayCommand.OpenSettings)
                && trayCommands.Contains(TrayCommand.OpenHistory)
                && trayCommands.Contains(TrayCommand.OpenModels)
                && trayCommands.Contains(TrayCommand.OpenStatus)
                && trayCommands.Contains(TrayCommand.Exit);
            var firstRunCompositionReady = settingsReady && modelsReady && historyReady && trayReady;

            var identity = Environment.GetEnvironmentVariable("BUTCHI_RELEASE_PROBE_PACKAGE_IDENTITY") ?? "Butchi";
            var version = Environment.GetEnvironmentVariable("BUTCHI_RELEASE_PROBE_PACKAGE_VERSION")
                ?? typeof(ReleaseProbe).Assembly.GetName().Version?.ToString(4)
                ?? "0.0.0.0";
            result = ReleaseProbeResult.CreateSuccess(
                identity,
                version,
                historyEntryCount: historyEntries.Count,
                firstRunCompositionReady: firstRunCompositionReady,
                trayReady: trayReady,
                settingsReady: settingsReady,
                modelsReady: modelsReady,
                historyReady: historyReady);
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
    bool ConfigReadable,
    bool HistoryReadable,
    int HistoryEntryCount,
    bool FirstRunCompositionReady,
    bool TrayReady,
    bool SettingsReady,
    bool ModelsReady,
    bool HistoryReady,
    string? ErrorCode,
    string? SelectedText,
    string? PromptContent,
    string? HistoryContent)
{
    public static ReleaseProbeResult CreateSuccess(
        string packageIdentity,
        string packageVersion,
        bool configReadable = true,
        bool historyReadable = true,
        int historyEntryCount = 0,
        bool firstRunCompositionReady = true,
        bool trayReady = true,
        bool settingsReady = true,
        bool modelsReady = true,
        bool historyReady = true) =>
        new(
            true,
            true,
            packageIdentity,
            packageVersion,
            configReadable,
            historyReadable,
            historyEntryCount,
            firstRunCompositionReady,
            trayReady,
            settingsReady,
            modelsReady,
            historyReady,
            null,
            null,
            null,
            null);

    public static ReleaseProbeResult Failure(string errorCode) =>
        new(false, false, string.Empty, string.Empty, false, false, 0, false, false, false, false, false, errorCode, null, null, null);
}
