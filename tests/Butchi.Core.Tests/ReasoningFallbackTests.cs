using Butchi.Core.Actions;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Xunit;

namespace Butchi.Core.Tests;

public sealed class ReasoningFallbackTests
{
    [Fact]
    public async Task Reasoning_only_generation_retries_without_thinking_and_returns_final_answer()
    {
        var engine = new ReasoningThenAnswerEngine();
        await using var scheduler = new TextActionScheduler(engine, new NoopResultSink());
        var reasoning = new List<string>();

        var result = await scheduler.RunAsync(
            TextAction.Rewrite,
            "SQLite is not required.",
            AppConfig.Default,
            InputOrigin.Selection,
            CancellationToken.None,
            new TextActionRunCallbacks(
                ReasoningChunk: (_, chunk) => reasoning.Add(chunk)));

        Assert.Equal("SQLite is optional.", result.Output);
        Assert.Equal(2, engine.Requests.Count);
        Assert.Contains("/no_think", engine.Requests[1].Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["analysis until token budget"], reasoning);
    }

    private sealed class ReasoningThenAnswerEngine : IInferenceEngine
    {
        public List<InferenceRequest> Requests { get; } = [];

        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<string> GenerateAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }

        public async IAsyncEnumerable<InferenceStreamChunk> GenerateDetailedAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            if (Requests.Count == 1)
            {
                yield return new InferenceStreamChunk(
                    InferenceStreamChunkKind.Reasoning,
                    "analysis until token budget");
                yield break;
            }

            yield return new InferenceStreamChunk(InferenceStreamChunkKind.Answer, "SQLite is optional.");
        }

        public InferenceStatus GetStatus() => new(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopResultSink : IResultActionSink
    {
        public Task CopyAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReplaceAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
