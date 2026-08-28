using System.Collections.Concurrent;
using Butchi.App.Popover;
using Butchi.App.Settings;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class PopoverActionControllerTests
{
    [Fact]
    public async Task Selecting_translate_runs_scheduler_and_streams_result()
    {
        var config = AppConfig.Default with { TargetLanguage = "Japanese", ResultAction = ResultAction.None };
        var store = new FakeConfigStore(config);
        var engine = new RecordingEngine("こん", "にちは");
        var sink = new RecordingSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var vm = new PopoverViewModel();
        vm.SetSession("hello", TextAction.Rewrite, "Japanese");
        await using var controller = new PopoverActionController(vm, scheduler, store, sink, action => action());

        vm.SelectAction(TextAction.Translate);
        await WaitUntilAsync(() => vm.Translate.RunId > 0 && !vm.Translate.IsRunning);

        Assert.Contains("hello", Assert.Single(engine.Requests).Prompt, StringComparison.Ordinal);
        Assert.Contains("Japanese", engine.Requests.Single().Prompt, StringComparison.Ordinal);
        Assert.Equal("こんにちは", vm.Translate.Output);
        Assert.Null(vm.Translate.ErrorMessage);
    }

    [Fact]
    public async Task Selecting_rewrite_runs_scheduler_and_streams_result()
    {
        var store = new FakeConfigStore(AppConfig.Default with { ResultAction = ResultAction.None });
        var engine = new RecordingEngine("clear ", "text");
        var sink = new RecordingSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var vm = new PopoverViewModel();
        vm.SetSession("rough text", TextAction.Translate, "Vietnamese");
        await using var controller = new PopoverActionController(vm, scheduler, store, sink, action => action());

        vm.SelectAction(TextAction.Rewrite);
        await WaitUntilAsync(() => vm.Rewrite.RunId > 0 && !vm.Rewrite.IsRunning);

        Assert.Contains("rough text", Assert.Single(engine.Requests).Prompt, StringComparison.Ordinal);
        Assert.Equal("clear text", vm.Rewrite.Output);
        Assert.Null(vm.Rewrite.ErrorMessage);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Controller did not complete the popover run.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeConfigStore(AppConfig config) : IAppConfigStore
    {
        public AppConfig Current { get; private set; } = config;
        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Current);
        public ValueTask SaveAsync(AppConfig value, CancellationToken cancellationToken)
        {
            Current = value;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingEngine(params string[] chunks) : IInferenceEngine
    {
        public ConcurrentQueue<InferenceRequest> Requests { get; } = new();
        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<string> GenerateAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
        public InferenceStatus GetStatus() => new(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSink : IResultActionSink
    {
        public Task CopyAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReplaceAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
