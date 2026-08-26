using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Xunit;

namespace Butchi.Infrastructure.Tests;

public sealed class LocalAiDataManagerTests
{
    [Fact]
    public async Task Clear_models_unloads_engine_before_deleting_directory()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureDirectories();
        var modelPath = paths.ModelPath("repo/model", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        await File.WriteAllTextAsync(modelPath, "model");

        var engine = new RecordingEngine(() => Directory.Exists(paths.ModelsDirectory));
        var manager = new LocalAiDataManager(paths, engine);

        await manager.ClearModelsAsync(CancellationToken.None);

        Assert.True(engine.UnloadCalled);
        Assert.True(engine.ModelsDirectoryExistedWhenUnloadWasCalled);
        Assert.True(Directory.Exists(paths.ModelsDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.ModelsDirectory));
    }

    private sealed class RecordingEngine(Func<bool> modelsDirectoryExists) : IInferenceEngine
    {
        public bool UnloadCalled { get; private set; }
        public bool ModelsDirectoryExistedWhenUnloadWasCalled { get; private set; }

        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UnloadAsync(CancellationToken cancellationToken)
        {
            UnloadCalled = true;
            ModelsDirectoryExistedWhenUnloadWasCalled = modelsDirectoryExists();
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> GenerateAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public InferenceStatus GetStatus() => new(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "butchi-clear-model-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
