using Avalonia.Threading;
using Butchi.App.Settings;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;

namespace Butchi.App.Popover;

public sealed class PopoverActionController : IAsyncDisposable
{
    private readonly PopoverViewModel _viewModel;
    private readonly TextActionScheduler _scheduler;
    private readonly IAppConfigStore _configStore;
    private readonly IResultActionSink _resultSink;
    private readonly Action<Action> _dispatch;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _tasksLock = new();
    private readonly HashSet<Task> _tasks = [];
    private int _disposed;

    public PopoverActionController(
        PopoverViewModel viewModel,
        TextActionScheduler scheduler,
        IAppConfigStore configStore,
        IResultActionSink resultSink,
        Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(configStore);
        ArgumentNullException.ThrowIfNull(resultSink);

        _viewModel = viewModel;
        _scheduler = scheduler;
        _configStore = configStore;
        _resultSink = resultSink;
        _dispatch = dispatch ?? DispatchToUiThread;

        _viewModel.ActionRequested += OnActionRequested;
    }

    private void OnActionRequested(object? sender, TextAction action) =>
        Track(RunAsync(action));

    private async Task RunAsync(TextAction action)
    {
        if (_lifetime.IsCancellationRequested || string.IsNullOrWhiteSpace(_viewModel.SourceText))
            return;

        try
        {
            var config = await _configStore.LoadAsync(_lifetime.Token).ConfigureAwait(false);
            if (action == TextAction.Translate)
            {
                var language = AppConfig.NormalizeTargetLanguage(_viewModel.TargetLanguage ?? config.TargetLanguage);
                config = config with { TargetLanguage = language };
            }

            var result = await _scheduler.RunAsync(
                action,
                _viewModel.SourceText,
                config,
                InputOrigin.Selection,
                _lifetime.Token,
                new TextActionRunCallbacks(
                    Started: runId => _dispatch(() => _viewModel.Begin(action, runId)),
                    Chunk: (runId, chunk) => _dispatch(() =>
                    {
                        if (_viewModel.Append(action, runId, chunk))
                            _viewModel.FlushPendingUpdates();
                    }))).ConfigureAwait(false);

            if (!result.IsObsolete)
                _dispatch(() => _viewModel.Complete(action, result.RunId));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void Track(Task task)
    {
        lock (_tasksLock)
            _tasks.Add(task);

        _ = task.ContinueWith(
            completed =>
            {
                lock (_tasksLock)
                    _tasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void DispatchToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _viewModel.ActionRequested -= OnActionRequested;
        _lifetime.Cancel();

        Task[] pending;
        lock (_tasksLock)
            pending = [.. _tasks];

        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _lifetime.Dispose();
        }
    }
}
