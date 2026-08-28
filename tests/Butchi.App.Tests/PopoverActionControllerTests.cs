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

    [Fact]
    public async Task Run_again_reloads_config_and_reruns_selected_action()
    {
        var store = new FakeConfigStore(AppConfig.Default with { ResultAction = ResultAction.None });
        var engine = new RecordingEngine("ok");
        var sink = new RecordingSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var vm = new PopoverViewModel();
        vm.SetSession("hello", TextAction.Translate, "Vietnamese");
        await using var controller = new PopoverActionController(vm, scheduler, store, sink, action => action());

        vm.SelectAction(TextAction.Translate);
        await WaitUntilAsync(() => engine.Requests.Count == 1 && !vm.Translate.IsRunning);
        store.Current = store.Current with { Temperature = 0.77f };

        vm.RequestRerun();
        await WaitUntilAsync(() => engine.Requests.Count == 2 && vm.Translate.RunId == 2 && !vm.Translate.IsRunning);

        Assert.Equal(2, store.LoadCount);
        Assert.Equal(0.77f, engine.Requests.Last().Temperature);
    }

    [Fact]
    public async Task Changing_language_persists_and_reruns_only_translate()
    {
        var store = new FakeConfigStore(AppConfig.Default with { ResultAction = ResultAction.None });
        var engine = new RecordingEngine("ok");
        var sink = new RecordingSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var vm = new PopoverViewModel();
        vm.SetSession("hello", TextAction.Translate, "Vietnamese");
        await using var controller = new PopoverActionController(vm, scheduler, store, sink, action => action());

        vm.RequestFavoriteLanguage("Japanese");
        await WaitUntilAsync(() => store.SaveCount == 1 && engine.Requests.Count == 1 && !vm.Translate.IsRunning);

        Assert.Equal("Japanese", store.Current.TargetLanguage);
        Assert.Contains("Japanese", engine.Requests.Single().Prompt, StringComparison.Ordinal);

        vm.SetSession("hello", TextAction.Rewrite, "Japanese");
        vm.RequestFavoriteLanguage("English");
        await WaitUntilAsync(() => store.SaveCount == 2);
        await Task.Delay(50);

        Assert.Equal("English", store.Current.TargetLanguage);
        Assert.Single(engine.Requests);
    }

    [Fact]
    public async Task Empty_source_shows_error_without_invoking_inference()
    {
        var store = new FakeConfigStore(AppConfig.Default with { ResultAction = ResultAction.None });
        var engine = new RecordingEngine("unused");
        var sink = new RecordingSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var vm = new PopoverViewModel();
        vm.SetSession("   ", TextAction.Translate, "Vietnamese");
        await using var controller = new PopoverActionController(vm, scheduler, store, sink, action => action());

        vm.SelectAction(TextAction.Translate);
        await WaitUntilAsync(() => vm.Translate.ErrorMessage is not null);

        Assert.Empty(engine.Requests);
        Assert.Contains("selected text", vm.Translate.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inference_failure_fails_started_run_and_remains_rerunnable()
    {
        var store = new FakeConfigStore(AppConfig.Default with { ResultAction = ResultAction.None });
        var engine = new FailingThenSuccessEngine();
        var sink = new RecordingSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var vm = new PopoverViewModel();
        vm.SetSession("hello", TextAction.Translate, "Vietnamese");
        await using var controller = new PopoverActionController(vm, scheduler, store, sink, action => action());

        vm.SelectAction(TextAction.Translate);
        await WaitUntilAsync(() => vm.Translate.ErrorMessage is not null);

        Assert.False(vm.Translate.IsRunning);
        Assert.Equal(1, vm.Translate.RunId);

        vm.RequestRerun();
        await WaitUntilAsync(() => vm.Translate.RunId == 2 && !vm.Translate.IsRunning && vm.Translate.Output == "recovered");

        Assert.Null(vm.Translate.ErrorMessage);
    }

    [Fact]
    public async Task Newer_translate_run_cannot_be_overwritten_by_obsolete_run()
    {
        var store = new FakeConfigStore(AppConfig.Default with { ResultAction = ResultAction.None });
        var engine = new SupersedingEngine();
        var sink = new RecordingSink();
        await using var scheduler = new TextActionScheduler(engine, sink);
        var vm = new PopoverViewModel();
        vm.SetSession("first", TextAction.Translate, "Vietnamese");
        await using var controller = new PopoverActionController(vm, scheduler, store, sink, action => action());

        vm.SelectAction(TextAction.Translate);
        await engine.FirstStarted.Task;
        vm.SetSession("second", TextAction.Translate, "Vietnamese");
        vm.SelectAction(TextAction.Translate);
        engine.ReleaseFirst.TrySetResult();

        await WaitUntilAsync(() => vm.Translate.RunId == 2 && !vm.Translate.IsRunning);

        Assert.Equal("second-result", vm.Translate.Output);
        Assert.Null(vm.Translate.ErrorMessage);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Controller did not reach the expected state.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeConfigStore(AppConfig config) : IAppConfigStore
    {
        public AppConfig Current { get; set; } = config;
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }
        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return ValueTask.FromResult(Current);
        }
        public ValueTask SaveAsync(AppConfig value, CancellationToken cancellationToken)
        {
            SaveCount++;
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

    private sealed class FailingThenSuccessEngine : IInferenceEngine
    {
        private int _runs;
        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<string> GenerateAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Interlocked.Increment(ref _runs) == 1)
                throw new InvalidOperationException("No model is loaded.");
            yield return "recovered";
        }
        public InferenceStatus GetStatus() => new(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SupersedingEngine : IInferenceEngine
    {
        private int _runs;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<string> GenerateAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var run = Interlocked.Increment(ref _runs);
            if (run == 1)
            {
                FirstStarted.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
                yield return "first-result";
                yield break;
            }

            await Task.Yield();
            yield return "second-result";
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
