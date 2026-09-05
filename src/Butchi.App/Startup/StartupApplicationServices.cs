using Butchi.App.History;
using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.Core.Platform;
using Butchi.Inference;
using Butchi.Infrastructure;

namespace Butchi.App.Startup;

public sealed class StartupApplicationServices : IAsyncDisposable
{
    private int _disposed;

    public StartupApplicationServices(string? dataDirectory = null)
    {
        Paths = new AppPaths(dataDirectory);
        Paths.EnsureDirectories();
        ConfigStore = new JsonAppConfigStoreAdapter(new JsonConfigStore(Paths));
        HttpClient = new HttpClient();
        InferenceEngine = new LLamaSharpInferenceEngine(
            new LLamaSharpRuntimeFactory(request => Paths.ModelPath(request.ModelRepo, request.ModelFile)));
        var downloader = new ModelDownloader(new HuggingFaceModelDownloadSource(HttpClient));
        ModelManager = new FileModelManager(Paths, downloader, InferenceEngine, ConfigStore);
        HistoryStore = new SqliteHistoryStoreAdapter(new SqliteHistoryStore(Paths));

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the Butchi executable path.");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AutoStartService = AutoStartServiceFactory.Create(
            executablePath,
            userProfile,
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
    }

    public AppPaths Paths { get; }
    public JsonAppConfigStoreAdapter ConfigStore { get; }
    public HttpClient HttpClient { get; }
    public LLamaSharpInferenceEngine InferenceEngine { get; }
    public FileModelManager ModelManager { get; }
    public IHistoryStore HistoryStore { get; }
    public IAutoStartService AutoStartService { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            await InferenceEngine.DisposeAsync();
        }
        finally
        {
            HttpClient.Dispose();
        }
    }
}
