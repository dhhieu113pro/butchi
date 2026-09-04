using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Xunit;

namespace Butchi.Inference.Tests;

public sealed class ReasoningOutputFilteringTests
{
    [Fact]
    public async Task Leading_think_block_is_not_exposed_even_when_tags_split_across_chunks()
    {
        var runtime = new FakeRuntime(["<thi", "nk>\ninternal reasoning\n</th", "ink>\n\n", "translated text"]);
        await using var engine = new LLamaSharpInferenceEngine(new FakeRuntimeFactory(runtime));
        await engine.LoadAsync(AppConfig.Default, CancellationToken.None);

        var chunks = await CollectAsync(
            engine.GenerateAsync(new InferenceRequest("prompt", 32, 0.2f, 42), CancellationToken.None));

        Assert.Equal(["translated text"], chunks);
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

    private sealed class FakeRuntimeFactory(ILLamaRuntime runtime) : ILLamaRuntimeFactory
    {
        public Task<ILLamaRuntime> LoadAsync(ModelLoadRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(runtime);
    }

    private sealed class FakeRuntime(IReadOnlyList<string> chunks) : ILLamaRuntime
    {
        public async IAsyncEnumerable<string> GenerateAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
                await Task.Yield();
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
