using Butchi.App.Composition;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class CompositionTests
{
    [Fact]
    public void Resolves_one_shared_inference_engine_instance()
    {
        var engine = new FakeInferenceEngine();
        using var services = ButchiAppServices.CreateForTesting(engine);

        var first = services.GetService(typeof(IInferenceEngine));
        var second = services.GetService(typeof(IInferenceEngine));

        Assert.Same(engine, first);
        Assert.Same(first, second);
    }

    private sealed class FakeInferenceEngine : IInferenceEngine
    {
        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<string> GenerateAsync(InferenceRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
        public InferenceStatus GetStatus() => new(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
