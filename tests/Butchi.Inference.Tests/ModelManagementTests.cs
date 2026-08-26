using Butchi.Inference;
using Xunit;

namespace Butchi.Inference.Tests;

public sealed class ModelManagementTests
{
    [Fact]
    public void Catalog_matches_reference_Butchi_models()
    {
        var models = ModelCatalog.Options;

        Assert.Collection(models,
            m =>
            {
                Assert.Equal("qwen35-0.8b-q4", m.Id);
                Assert.Equal("Qwen3.5 0.8B (Q4_K_M) — default", m.Label);
                Assert.Equal("unsloth/Qwen3.5-0.8B-GGUF", m.Repo);
                Assert.Equal("Qwen3.5-0.8B-Q4_K_M.gguf", m.File);
                Assert.Equal("~530 MB", m.SizeHint);
            },
            m =>
            {
                Assert.Equal("qwen35-0.8b-q5", m.Id);
                Assert.Equal("unsloth/Qwen3.5-0.8B-GGUF", m.Repo);
                Assert.Equal("Qwen3.5-0.8B-Q5_K_M.gguf", m.File);
                Assert.Equal("~590 MB", m.SizeHint);
            },
            m =>
            {
                Assert.Equal("qwen3-0.6b-q4", m.Id);
                Assert.Equal("unsloth/Qwen3-0.6B-GGUF", m.Repo);
                Assert.Equal("Qwen3-0.6B-Q4_K_M.gguf", m.File);
                Assert.Equal("~400 MB", m.SizeHint);
            });
    }

    [Fact]
    public async Task Successful_download_uses_temp_file_then_atomically_promotes()
    {
        using var temp = new TempDirectory();
        var finalPath = Path.Combine(temp.Path, "repo", "model.gguf");
        var source = new FakeDownloadSource("hello model"u8.ToArray());
        var downloader = new ModelDownloader(source);
        var progress = new List<ModelDownloadProgress>();

        var result = await downloader.DownloadAsync(
            "unsloth/repo", "model.gguf", finalPath,
            new Progress<ModelDownloadProgress>(progress.Add), CancellationToken.None);

        Assert.Equal(finalPath, result);
        Assert.True(File.Exists(finalPath));
        Assert.Equal("hello model", await File.ReadAllTextAsync(finalPath));
        Assert.False(File.Exists(finalPath + ".download"));
        Assert.Equal(1, source.OpenCount);
    }

    [Fact]
    public async Task Cancelled_download_never_leaves_corrupt_final_file_or_temp_file()
    {
        using var temp = new TempDirectory();
        var finalPath = Path.Combine(temp.Path, "model.gguf");
        var source = new CancellingDownloadSource();
        var downloader = new ModelDownloader(source);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            downloader.DownloadAsync("repo", "model.gguf", finalPath, progress: null, CancellationToken.None));

        Assert.False(File.Exists(finalPath));
        Assert.False(File.Exists(finalPath + ".download"));
    }

    [Fact]
    public async Task Existing_final_file_is_not_exposed_until_replacement_download_completes()
    {
        using var temp = new TempDirectory();
        var finalPath = Path.Combine(temp.Path, "model.gguf");
        await File.WriteAllTextAsync(finalPath, "old");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new GatedDownloadSource("new"u8.ToArray(), gate.Task);
        var downloader = new ModelDownloader(source);

        var task = downloader.DownloadAsync("repo", "model.gguf", finalPath, null, CancellationToken.None);
        await source.ReadStarted.Task;

        Assert.Equal("old", await File.ReadAllTextAsync(finalPath));
        Assert.True(File.Exists(finalPath + ".download"));

        gate.SetResult();
        await task;

        Assert.Equal("new", await File.ReadAllTextAsync(finalPath));
        Assert.False(File.Exists(finalPath + ".download"));
    }

    private sealed class FakeDownloadSource(byte[] bytes) : IModelDownloadSource
    {
        public int OpenCount { get; private set; }
        public Task<ModelDownloadStream> OpenAsync(string repo, string file, CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.FromResult(new ModelDownloadStream(new MemoryStream(bytes), bytes.Length));
        }
    }

    private sealed class CancellingDownloadSource : IModelDownloadSource
    {
        public Task<ModelDownloadStream> OpenAsync(string repo, string file, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelDownloadStream(new CancellingStream(), 1024));
    }

    private sealed class CancellingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new OperationCanceledException());
    }

    private sealed class GatedDownloadSource(byte[] bytes, Task gate) : IModelDownloadSource
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ModelDownloadStream> OpenAsync(string repo, string file, CancellationToken cancellationToken) =>
            Task.FromResult<ModelDownloadStream>(new(new GatedReadStream(bytes, gate, ReadStarted), bytes.Length));
    }

    private sealed class GatedReadStream(byte[] bytes, Task gate, TaskCompletionSource readStarted) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            readStarted.TrySetResult();
            await gate.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "butchi-model-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
