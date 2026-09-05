using Butchi.App.History;
using Butchi.App.Popover;
using Butchi.App.Settings;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;
using Butchi.Core.History;
using Butchi.Core.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class HistoryPersistenceContractTests
{
    [Fact]
    public void History_store_contract_supports_appending_completed_results()
    {
        var append = typeof(IHistoryStore)
            .GetMethods()
            .SingleOrDefault(method =>
                method.Name == "AppendAsync" &&
                method.GetParameters() is var parameters &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(HistoryEntry) &&
                parameters[1].ParameterType == typeof(CancellationToken));

        Assert.NotNull(append);
    }

    [Fact]
    public async Task Completed_translate_is_persisted_with_retention_and_language()
    {
        var config = AppConfig.Default with
        {
            ResultAction = ResultAction.None,
            HistoryRetentionDays = 30,
            TargetLanguage = "Japanese"
        };
        var configStore = new FakeConfigStore(config);
        var history = new RecordingHistoryStore();
        var sink = new NullResultSink();
        await using var scheduler = new TextActionScheduler(new FixedEngine("こんにちは"), sink);
        var vm = new PopoverViewModel();
        vm.SetSession("hello", TextAction.Translate, "Japanese");
        await using var controller = CreateController(vm, scheduler, configStore, sink, history);

        vm.SelectAction(TextAction.Translate);
        await WaitUntilAsync(() => history.Entries.Count == 1 && !vm.Translate.IsRunning);

        var entry = Assert.Single(history.Entries);
        Assert.Equal("translate", entry.Action);
        Assert.Equal("hello", entry.Source);
        Assert.Equal("こんにちは", entry.Result);
        Assert.Equal("Completed locally", entry.Message);
        Assert.Equal("Japanese", entry.TargetLanguage);
        Assert.True(entry.TimestampMs > 0);
        Assert.Equal((30, entry.TimestampMs), history.LastRetention);
    }

    [Fact]
    public async Task History_write_failure_does_not_fail_completed_action()
    {
        var configStore = new FakeConfigStore(AppConfig.Default with
        {
            ResultAction = ResultAction.None,
            HistoryRetentionDays = 30
        });
        var history = new RecordingHistoryStore { ThrowOnAppend = true };
        var sink = new NullResultSink();
        await using var scheduler = new TextActionScheduler(new FixedEngine("clear text"), sink);
        var vm = new PopoverViewModel();
        vm.SetSession("rough text", TextAction.Rewrite, "Vietnamese");
        await using var controller = CreateController(vm, scheduler, configStore, sink, history);

        vm.SelectAction(TextAction.Rewrite);
        await WaitUntilAsync(() => !vm.Rewrite.IsRunning && vm.Rewrite.Output == "clear text");

        Assert.Null(vm.Rewrite.ErrorMessage);
    }

    [Fact]
    public async Task Zero_retention_disables_history_persistence()
    {
        var configStore = new FakeConfigStore(AppConfig.Default with
        {
            ResultAction = ResultAction.None,
            HistoryRetentionDays = 0
        });
        var history = new RecordingHistoryStore();
        var sink = new NullResultSink();
        await using var scheduler = new TextActionScheduler(new FixedEngine("clear text"), sink);
        var vm = new PopoverViewModel();
        vm.SetSession("rough text", TextAction.Rewrite, "Vietnamese");
        await using var controller = CreateController(vm, scheduler, configStore, sink, history);

        vm.SelectAction(TextAction.Rewrite);
        await WaitUntilAsync(() => !vm.Rewrite.IsRunning && vm.Rewrite.Output == "clear text");

        Assert.Empty(history.Entries);
        Assert.Null(history.LastRetention);
    }

    private static PopoverActionController CreateController(
        PopoverViewModel viewModel,
        TextActionScheduler scheduler,
        IAppConfigStore configStore,
        IResultActionSink resultSink,
        IHistoryStore historyStore)
    {
        var constructor = typeof(PopoverActionController)
            .GetConstructors()
            .SingleOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 6 &&
                       parameters[0].ParameterType == typeof(PopoverViewModel) &&
                       parameters[1].ParameterType == typeof(TextActionScheduler) &&
                       parameters[2].ParameterType == typeof(IAppConfigStore) &&
                       parameters[3].ParameterType == typeof(IResultActionSink) &&
                       parameters[4].ParameterType == typeof(Action<Action>) &&
                       parameters[5].ParameterType == typeof(IHistoryStore);
            });

        Assert.NotNull(constructor);
        return (PopoverActionController)constructor.Invoke(
            [viewModel, scheduler, configStore, resultSink, (Action<Action>)(action => action()), historyStore]);
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

    private sealed class FakeConfigStore(AppConfig value) : IAppConfigStore
    {
        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(value);
        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class RecordingHistoryStore : IHistoryStore
    {
        public List<HistoryEntry> Entries { get; } = [];
        public (int Days, long NowMs)? LastRetention { get; private set; }
        public bool ThrowOnAppend { get; init; }

        public ValueTask AppendAsync(HistoryEntry entry, CancellationToken cancellationToken)
        {
            if (ThrowOnAppend)
                throw new InvalidOperationException("history write failed");
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<HistoryEntry>> SearchAsync(string? query, string? action, int? limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<HistoryEntry>>([.. Entries]);

        public ValueTask DeleteAsync(string id, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ApplyRetentionAsync(int retentionDays, long nowMs, CancellationToken cancellationToken)
        {
            LastRetention = (retentionDays, nowMs);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NullResultSink : IResultActionSink
    {
        public Task CopyAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReplaceAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedEngine(string output) : IInferenceEngine
    {
        public Task LoadAsync(AppConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<string> GenerateAsync(
            InferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return output;
        }

        public InferenceStatus GetStatus() => new(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
