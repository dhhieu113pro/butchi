using Butchi.App.Settings;
using Butchi.Core.Inference;
using Butchi.Inference;
using Butchi.Infrastructure;

namespace Butchi.App.Models;

public interface IModelManager
{
    IReadOnlyList<ModelOption> Catalog { get; }
    bool IsDownloaded(ModelOption model);
    InferenceStatus GetStatus();
    ValueTask DownloadAsync(ModelOption model, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken);
    ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken);
    ValueTask UnloadAsync(CancellationToken cancellationToken);
    ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken);
}

public sealed record ModelItemState(ModelOption Model, bool IsDownloaded, bool IsLoaded);

public sealed class ModelsViewModel(IModelManager manager)
{
    public IReadOnlyList<ModelItemState> Items { get; private set; } = [];
    public string? ActualBackend { get; private set; }
    public string? ActualDevice { get; private set; }
    public ModelDownloadProgress? DownloadProgress { get; private set; }

    public ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = manager.GetStatus();
        Items = manager.Catalog.Select(model => new ModelItemState(
            model,
            manager.IsDownloaded(model),
            status.IsLoaded && status.ModelRepo == model.Repo && status.ModelFile == model.File)).ToArray();
        ActualBackend = status.ActualBackend;
        ActualDevice = status.ActualDevice;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DownloadAsync(ModelOption model, CancellationToken cancellationToken)
    {
        var progress = new Progress<ModelDownloadProgress>(value => DownloadProgress = value);
        await manager.DownloadAsync(model, progress, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken)
    {
        await manager.LoadAsync(model, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken)
    {
        var status = manager.GetStatus();
        if (status.IsLoaded && status.ModelRepo == model.Repo && status.ModelFile == model.File)
            await manager.UnloadAsync(cancellationToken);

        await manager.DeleteAsync(model, cancellationToken);
        await RefreshAsync(cancellationToken);
    }
}

public static class FirstRunPolicy
{
    public static bool ShouldOpenModels(InferenceStatus status, bool configuredModelExists) =>
        !status.IsLoaded && !configuredModelExists;
}

public sealed class FileModelManager(
    AppPaths paths,
    ModelDownloader downloader,
    IInferenceEngine inferenceEngine,
    IAppConfigStore configStore) : IModelManager
{
    public IReadOnlyList<ModelOption> Catalog => ModelCatalog.Options;

    public bool IsDownloaded(ModelOption model) => File.Exists(paths.ModelPath(model.Repo, model.File));

    public InferenceStatus GetStatus() => inferenceEngine.GetStatus();

    public async ValueTask DownloadAsync(ModelOption model, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        await downloader.DownloadAsync(model.Repo, model.File, paths.ModelPath(model.Repo, model.File), progress, cancellationToken);
    }

    public async ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        config = config with { ModelRepo = model.Repo, ModelFile = model.File };
        await configStore.SaveAsync(config, cancellationToken);
        await inferenceEngine.LoadAsync(config, cancellationToken);
    }

    public async ValueTask UnloadAsync(CancellationToken cancellationToken) =>
        await inferenceEngine.UnloadAsync(cancellationToken);

    public ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = paths.ModelPath(model.Repo, model.File);
        if (File.Exists(path)) File.Delete(path);
        return ValueTask.CompletedTask;
    }
}
