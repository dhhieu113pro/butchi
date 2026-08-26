using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Xunit;

namespace Butchi.Inference.Tests;

public sealed class LLamaSharpInferenceEngineTests
{
    [Fact]
    public async Task Equivalent_requests_reuse_one_loaded_model()
    {
        var runtime = new FakeRuntime(["A"]);
        var factory = new FakeRuntimeFactory(runtime);
        await using var engine = new LLamaSharpInferenceEngine(factory);
        var config = AppConfig.Default;

        await engine.LoadAsync(config, CancellationToken.None);
        _ = await CollectAsync(engine.GenerateAsync(new InferenceRequest("prompt", 32, 0.3f, 42), CancellationToken.None));
        _ = await CollectAsync(engine.GenerateAsync(new InferenceRequest("prompt2", 32, 0.3f, 42), CancellationToken.None));

        Assert.Equal(1, factory.LoadCount);
        Assert.Equal(2, runtime.GenerateCount);
        Assert.True(engine.GetStatus().IsLoaded);
    }

    [Fact]
    public async Task Loading_same_effective_configuration_is_idempotent()
    {
        var factory = new FakeRuntimeFactory(new FakeRuntime(["ok"]));
        await using var engine = new LLamaSharpInferenceEngine(factory);

        await engine.LoadAsync(AppConfig.Default, CancellationToken.None);
        await engine.LoadAsync(AppConfig.Default, CancellationToken.None);

        Assert.Equal(1, factory.LoadCount);
        Assert.Equal(0, factory.DisposeCount);
    }

    [Fact]
    public async Task Model_backend_or_context_change_reloads_weights_once()
    {
        var factory = new FakeRuntimeFactory(new FakeRuntime(["ok"]));
        await using var engine = new LLamaSharpInferenceEngine(factory);

        await engine.LoadAsync(AppConfig.Default, CancellationToken.None);
        await engine.LoadAsync(AppConfig.Default with { ModelFile = "other.gguf" }, CancellationToken.None);
        await engine.LoadAsync(AppConfig.Default with { ModelFile = "other.gguf", GpuLayers = 12 }, CancellationToken.None);

        Assert.Equal(3, factory.LoadCount);
        Assert.Equal(2, factory.DisposeCount);
    }

    [Fact]
    public async Task Streaming_preserves_chunk_order()
    {
        var runtime = new FakeRuntime(["A", "B", "C"]);
        await using var engine = new LLamaSharpInferenceEngine(new FakeRuntimeFactory(runtime));
        await engine.LoadAsync(AppConfig.Default, CancellationToken.None);

        var chunks = await CollectAsync(engine.GenerateAsync(new InferenceRequest("prompt", 10, 0.2f, 42), CancellationToken.None));

        Assert.Equal(["A", "B", "C"], chunks);
    }

    [Fact]
    public async Task Cancellation_stops_generation_without_unloading_model()
    {
        var runtime = new FakeRuntime(["A", "B", "C"]);
        await using var engine = new LLamaSharpInferenceEngine(new FakeRuntimeFactory(runtime));
        await engine.LoadAsync(AppConfig.Default, CancellationToken.None);
        using var cts = new CancellationTokenSource();

        var received = new List<string>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in engine.GenerateAsync(new InferenceRequest("prompt", 10, 0.2f, 42), cts.Token))
            {
                received.Add(chunk);
                if (chunk == "B")
                {
                    cts.Cancel();
                }
            }
        });

        Assert.Equal(["A", "B"], received);
        Assert.True(engine.GetStatus().IsLoaded);
    }

    [Fact]
    public async Task Explicit_unload_disposes_runtime_and_clears_status()
    {
        var factory = new FakeRuntimeFactory(new FakeRuntime(["ok"]));
        await using var engine = new LLamaSharpInferenceEngine(factory);
        await engine.LoadAsync(AppConfig.Default, CancellationToken.None);

        await engine.UnloadAsync(CancellationToken.None);

        Assert.False(engine.GetStatus().IsLoaded);
        Assert.Equal(1, factory.DisposeCount);
    }

    private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> source)
    {
        var result = new List<string>();
        await foreach (var item in source)
        {
            result.Add(item);
        }

        return result;
    }

    private sealed class FakeRuntimeFactory : ILLamaRuntimeFactory
    {
        private readonly FakeRuntime _runtime;

        public FakeRuntimeFactory(FakeRuntime runtime) => _runtime = runtime;

        public int LoadCount { get; private set; }
        public int DisposeCount => _runtime.DisposeCount;

        public Task<ILLamaRuntime> LoadAsync(ModelLoadRequest request, CancellationToken cancellationToken)
        {
            LoadCount++;
            _runtime.LoadRequest = request;
            return Task.FromResult<ILLamaRuntime>(_runtime);
        }
    }

    private sealed class FakeRuntime : ILLamaRuntime
    {
        private readonly IReadOnlyList<string> _chunks;

        public FakeRuntime(IReadOnlyList<string> chunks) => _chunks = chunks;

        public int GenerateCount { get; private set; }
        public int DisposeCount { get; private set; }
        public ModelLoadRequest? LoadRequest { get; set; }

        public async IAsyncEnumerable<string> GenerateAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            GenerateCount++;
            foreach (var chunk in _chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
                await Task.Yield();
            }
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
