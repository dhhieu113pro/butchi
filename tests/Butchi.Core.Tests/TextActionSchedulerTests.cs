using System.Collections.Concurrent;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Xunit;

namespace Butchi.Core.Tests;

public sealed class TextActionSchedulerTests
{
    [Fact]
    public async Task Run_callbacks_publish_scheduler_run_id_and_chunks_in_order()
    {
        var engine = new ChunkEngine("a", "b");
        var sink = new RecordingResultSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var events = new List<string>();

        var result = await scheduler.RunAsync(
            TextAction.Translate,
            "hello",
            AppConfig.Default,
            InputOrigin.Selection,
            CancellationToken.None,
            new TextActionRunCallbacks(
                Started: runId => events.Add($"started:{runId}"),
                Chunk: (runId, chunk) => events.Add($"chunk:{runId}:{chunk}")));

        Assert.Equal(1, result.RunId);
        Assert.Equal(["started:1", "chunk:1:a", "chunk:1:b"], events);
        Assert.Equal("ab", result.Output);
    }

    [Fact]
    public async Task Obsolete_run_stops_publishing_chunks_after_replacement()
    {
        var engine = new CallbackControllableEngine();
        var sink = new RecordingResultSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var firstEvents = new ConcurrentQueue<string>();
        var secondEvents = new ConcurrentQueue<string>();

        var first = scheduler.RunAsync(
            TextAction.Translate,
            "first",
            AppConfig.Default,
            InputOrigin.Selection,
            CancellationToken.None,
            new TextActionRunCallbacks(
                Started: runId => firstEvents.Enqueue($"started:{runId}"),
                Chunk: (runId, chunk) => firstEvents.Enqueue($"chunk:{runId}:{chunk}")));
        await engine.FirstGenerationStarted.Task;

        var second = scheduler.RunAsync(
            TextAction.Translate,
            "second",
            AppConfig.Default,
            InputOrigin.Selection,
            CancellationToken.None,
            new TextActionRunCallbacks(
                Started: runId => secondEvents.Enqueue($"started:{runId}"),
                Chunk: (runId, chunk) => secondEvents.Enqueue($"chunk:{runId}:{chunk}")));

        engine.Release.SetResult();
        var firstResult = await first;
        var secondResult = await second;

        Assert.True(firstResult.IsObsolete);
        Assert.DoesNotContain(firstEvents, x => x.Contains("first-result", StringComparison.Ordinal));
        Assert.False(secondResult.IsObsolete);
        Assert.Contains("started:2", secondEvents);
        Assert.Contains("chunk:2:second-result", secondEvents);
    }

    [Fact]
    public async Task Translate_and_rewrite_share_one_serial_inference_lane()
    {
        var engine = new RecordingEngine(TimeSpan.FromMilliseconds(40));
        var sink = new RecordingResultSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var config = AppConfig.Default;

        await Task.WhenAll(
            scheduler.RunAsync(TextAction.Translate, "hello", config, InputOrigin.Selection, CancellationToken.None),
            scheduler.RunAsync(TextAction.Rewrite, "hello", config, InputOrigin.Selection, CancellationToken.None));

        Assert.Equal(1, engine.MaxConcurrentGenerations);
        Assert.Equal(2, engine.Requests.Count);
    }

    [Fact]
    public async Task Newer_run_of_same_action_cancels_obsolete_run()
    {
        var engine = new ControllableEngine();
        var sink = new RecordingResultSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var config = AppConfig.Default;

        var first = scheduler.RunAsync(TextAction.Translate, "first", config, InputOrigin.Selection, CancellationToken.None);
        await engine.FirstGenerationStarted.Task;
        var second = scheduler.RunAsync(TextAction.Translate, "second", config, InputOrigin.Selection, CancellationToken.None);

        engine.Release.SetResult();
        var firstResult = await first;
        var secondResult = await second;

        Assert.True(firstResult.IsObsolete);
        Assert.False(secondResult.IsObsolete);
        Assert.Equal("second-result", secondResult.Output);
    }

    [Fact]
    public async Task Translate_and_rewrite_keep_independent_run_ids()
    {
        var engine = new EchoEngine();
        var sink = new RecordingResultSink();
        await using var scheduler = new TextActionScheduler(engine, sink);

        var translate = await scheduler.RunAsync(TextAction.Translate, "a", AppConfig.Default, InputOrigin.Selection, CancellationToken.None);
        var rewrite = await scheduler.RunAsync(TextAction.Rewrite, "b", AppConfig.Default, InputOrigin.Selection, CancellationToken.None);

        Assert.Equal(1, translate.RunId);
        Assert.Equal(1, rewrite.RunId);
    }

    [Fact]
    public async Task Single_enabled_action_with_copy_applies_automatic_copy()
    {
        var sink = new RecordingResultSink();
        await using var scheduler = new TextActionScheduler(new EchoEngine(), sink);
        var config = AppConfig.Default with { RewriteEnabled = false, ResultAction = ResultAction.Copy };

        var result = await scheduler.RunAsync(TextAction.Translate, "hello", config, InputOrigin.Selection, CancellationToken.None);

        Assert.Equal(result.Output, Assert.Single(sink.Copied));
        Assert.Empty(sink.Replaced);
    }

    [Fact]
    public async Task Single_enabled_action_with_replace_replaces_selected_text()
    {
        var sink = new RecordingResultSink();
        await using var scheduler = new TextActionScheduler(new EchoEngine(), sink);
        var config = AppConfig.Default with { RewriteEnabled = false, ResultAction = ResultAction.Replace };

        var result = await scheduler.RunAsync(TextAction.Translate, "hello", config, InputOrigin.Selection, CancellationToken.None);

        Assert.Equal(result.Output, Assert.Single(sink.Replaced));
        Assert.Empty(sink.Copied);
    }

    [Fact]
    public async Task Manual_input_never_auto_replaces_source_text()
    {
        var sink = new RecordingResultSink();
        await using var scheduler = new TextActionScheduler(new EchoEngine(), sink);
        var config = AppConfig.Default with { RewriteEnabled = false, ResultAction = ResultAction.Replace };

        await scheduler.RunAsync(TextAction.Translate, "typed manually", config, InputOrigin.Manual, CancellationToken.None);

        Assert.Empty(sink.Replaced);
        Assert.Empty(sink.Copied);
    }

    [Theory]
    [InlineData(ResultAction.None)]
    [InlineData(ResultAction.Copy)]
    [InlineData(ResultAction.Replace)]
    public async Task Multiple_enabled_actions_never_apply_automatic_result(ResultAction action)
    {
        var sink = new RecordingResultSink();
        await using var scheduler = new TextActionScheduler(new EchoEngine(), sink);
        var config = AppConfig.Default with { ResultAction = action };

        await scheduler.RunAsync(TextAction.Translate, "hello", config, InputOrigin.Selection, CancellationToken.None);

        Assert.Empty(sink.Copied);
        Assert.Empty(sink.Replaced);
    }

    private sealed class RecordingResultSink : IResultActionSink
    {
        public List<string> Copied { get; } = [];
        public List<string> Replaced { get; } = [];
        public Task CopyAsync(string text, CancellationToken cancellationToken) { Copied.Add(text); return Task.CompletedTask; }
        public Task ReplaceAsync(string text, CancellationToken cancellationToken) { Replaced.Add(text); return Task.CompletedTask; }
    }

    private sealed class EchoEngine : IInferenceEngine
    {
        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<string> GenerateAsync(InferenceRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return request.Prompt.Contains("Target language:", StringComparison.Ordinal) ? "translated" : "rewritten";
        }
        public InferenceStatus GetStatus() => new(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ChunkEngine(params string[] chunks) : IInferenceEngine
    {
        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<string> GenerateAsync(InferenceRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
        public InferenceStatus GetStatus() => new(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingEngine(TimeSpan delay) : IInferenceEngine
    {
        private int _active;
        public int MaxConcurrentGenerations { get; private set; }
        public ConcurrentQueue<InferenceRequest> Requests { get; } = new();
        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<string> GenerateAsync(InferenceRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            var active = Interlocked.Increment(ref _active);
            MaxConcurrentGenerations = Math.Max(MaxConcurrentGenerations, active);
            try { await Task.Delay(delay, cancellationToken); yield return "ok"; }
            finally { Interlocked.Decrement(ref _active); }
        }
        public InferenceStatus GetStatus() => new(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ControllableEngine : IInferenceEngine
    {
        private int _count;
        public TaskCompletionSource FirstGenerationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<string> GenerateAsync(InferenceRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var number = Interlocked.Increment(ref _count);
            if (number == 1) FirstGenerationStarted.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            yield return number == 1 ? "first-result" : "second-result";
        }
        public InferenceStatus GetStatus() => new(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CallbackControllableEngine : IInferenceEngine
    {
        private int _count;
        public TaskCompletionSource FirstGenerationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<string> GenerateAsync(InferenceRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var number = Interlocked.Increment(ref _count);
            if (number == 1)
            {
                FirstGenerationStarted.SetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            yield return number == 1 ? "first-result" : "second-result";
        }
        public InferenceStatus GetStatus() => new(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
