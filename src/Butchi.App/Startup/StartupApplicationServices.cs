using Butchi.App.History;
using Butchi.App.Models;
using Butchi.App.Settings;
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
    }

    public AppPaths Paths { get; }
    public JsonAppConfigStoreAdapter ConfigStore { get; }
    public HttpClient HttpClient { get; }
    public LLamaSharpInferenceEngine InferenceEngine { get; }
    public FileModelManager ModelManager { get; }
    public IHistoryStore HistoryStore { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await InferenceEngine.DisposeAsync();
        HttpClient.Dispose();
    }
}
