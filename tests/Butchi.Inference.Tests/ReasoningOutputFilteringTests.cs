using Butchi.Core.Actions;
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

    [Fact]
    public async Task Detailed_stream_separates_reasoning_from_answer_when_tags_are_split()
    {
        var runtime = new FakeRuntime(["<thi", "nk>\ninternal ", "reasoning\n</th", "ink>\n\n", "translated text"]);
        await using var engine = new LLamaSharpInferenceEngine(new FakeRuntimeFactory(runtime));
        await engine.LoadAsync(AppConfig.Default, CancellationToken.None);

        var detailedMethod = typeof(IInferenceEngine).GetMethod("GenerateDetailedAsync");
        Assert.NotNull(detailedMethod);
        var source = detailedMethod.Invoke(
            engine,
            [new InferenceRequest("prompt", 32, 0.2f, 42), CancellationToken.None]);
        Assert.NotNull(source);

        var chunks = await CollectDetailedAsync(source);

        Assert.Equal(
            [("Reasoning", "internal "), ("Reasoning", "reasoning"), ("Answer", "translated text")],
            chunks);
    }

    [Fact]
    public async Task Scheduler_keeps_reasoning_out_of_result_but_publishes_reasoning_callback()
    {
        var runtime = new FakeRuntime(["<think>\nworking it out\n</think>\n\n", "final answer"]);
        await using var engine = new LLamaSharpInferenceEngine(new FakeRuntimeFactory(runtime));
        await engine.LoadAsync(AppConfig.Default, CancellationToken.None);
        var sink = new RecordingResultSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var reasoning = new List<string>();

        var callbackConstructor = typeof(TextActionRunCallbacks).GetConstructors().Single();
        Assert.Equal(3, callbackConstructor.GetParameters().Length);
        var callbacks = (TextActionRunCallbacks)callbackConstructor.Invoke(
            [null, null, (Action<long, string>)((_, chunk) => reasoning.Add(chunk))]);

        var result = await scheduler.RunAsync(
            TextAction.Translate,
            "hello",
            AppConfig.Default,
            InputOrigin.Selection,
            CancellationToken.None,
            callbacks);

        Assert.Equal("final answer", result.Output);
        Assert.Equal(["working it out"], reasoning);
        Assert.Empty(sink.Copied);
        Assert.Empty(sink.Replaced);
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

    private static async Task<List<(string Kind, string Text)>> CollectDetailedAsync(object source)
    {
        var typedSource = Assert.IsAssignableFrom<IAsyncEnumerable<object>>(source);
        var result = new List<(string Kind, string Text)>();
        await foreach (var current in typedSource)
        {
            var type = current.GetType();
            var kind = type.GetProperty("Kind")?.GetValue(current)?.ToString();
            var text = type.GetProperty("Text")?.GetValue(current) as string;
            Assert.NotNull(kind);
            Assert.NotNull(text);
            result.Add((kind, text));
        }

        return result;
    }

    private sealed class RecordingResultSink : IResultActionSink
    {
        public List<string> Copied { get; } = [];
        public List<string> Replaced { get; } = [];
        public Task CopyAsync(string text, CancellationToken cancellationToken)
        {
            Copied.Add(text);
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(string text, CancellationToken cancellationToken)
        {
            Replaced.Add(text);
            return Task.CompletedTask;
        }
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
