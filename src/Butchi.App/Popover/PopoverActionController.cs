using Avalonia.Threading;
using Butchi.App.History;
using Butchi.App.Settings;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;
using Butchi.Core.History;

namespace Butchi.App.Popover;

public sealed class PopoverActionController : IAsyncDisposable
{
    private readonly PopoverViewModel _viewModel;
    private readonly TextActionScheduler _scheduler;
    private readonly IAppConfigStore _configStore;
    private readonly IResultActionSink _resultSink;
    private readonly IHistoryStore? _historyStore;
    private readonly Action<Action> _dispatch;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _tasksLock = new();
    private readonly HashSet<Task> _tasks = [];
    private long _presentationErrorRunId;
    private int _disposed;

    public PopoverActionController(
        PopoverViewModel viewModel,
        TextActionScheduler scheduler,
        IAppConfigStore configStore,
        IResultActionSink resultSink,
        Action<Action>? dispatch = null,
        IHistoryStore? historyStore = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(configStore);
        ArgumentNullException.ThrowIfNull(resultSink);

        _viewModel = viewModel;
        _scheduler = scheduler;
        _configStore = configStore;
        _resultSink = resultSink;
        _historyStore = historyStore;
        _dispatch = dispatch ?? DispatchToUiThread;

        _viewModel.ActionRequested += OnActionRequested;
        _viewModel.RerunRequested += OnRerunRequested;
        _viewModel.TranslateLanguageRequested += OnTranslateLanguageRequested;
        _viewModel.CopyRequested += OnCopyRequested;
        _viewModel.ReplaceRequested += OnReplaceRequested;
    }

    private void OnActionRequested(object? sender, TextAction action) =>
        Track(RunAsync(action));

    private void OnRerunRequested(object? sender, TextAction action) =>
        Track(RunAsync(action));

    private void OnTranslateLanguageRequested(object? sender, string language) =>
        Track(ChangeLanguageAsync(language));

    private void OnCopyRequested(object? sender, string text) =>
        Track(ApplyExplicitResultAsync(_viewModel.SelectedAction, text, replace: false));

    private void OnReplaceRequested(object? sender, string text) =>
        Track(ApplyExplicitResultAsync(_viewModel.SelectedAction, text, replace: true));

    private async Task ApplyExplicitResultAsync(TextAction action, string text, bool replace)
    {
        if (_lifetime.IsCancellationRequested)
            return;

        var state = action == TextAction.Translate ? _viewModel.Translate : _viewModel.Rewrite;
        var runId = state.RunId;

        try
        {
            if (replace)
                await _resultSink.ReplaceAsync(text, _lifetime.Token).ConfigureAwait(false);
            else
                await _resultSink.CopyAsync(text, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var operation = replace ? "replace" : "copy";
            _dispatch(() => _viewModel.Fail(action, runId, $"Could not {operation} result: {ex.Message}"));
        }
    }

    private async Task ChangeLanguageAsync(string language)
    {
        if (_lifetime.IsCancellationRequested)
            return;

        try
        {
            var normalized = AppConfig.NormalizeTargetLanguage(language);
            var config = await _configStore.LoadAsync(_lifetime.Token).ConfigureAwait(false);
            await _configStore.SaveAsync(
                config with { TargetLanguage = normalized },
                _lifetime.Token).ConfigureAwait(false);

            if (_viewModel.SelectedAction == TextAction.Translate)
                await RunAsync(TextAction.Translate).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            PresentError(_viewModel.SelectedAction, FriendlyMessage(ex));
        }
    }

    private async Task RunAsync(TextAction action)
    {
        if (_lifetime.IsCancellationRequested)
            return;

        var sourceText = _viewModel.SourceText;
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            PresentError(action, "No selected text is available. Select text and try again.");
            return;
        }

        long startedRunId = 0;
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
                sourceText,
                config,
                InputOrigin.Selection,
                _lifetime.Token,
                new TextActionRunCallbacks(
                    Started: runId =>
                    {
                        startedRunId = runId;
                        _dispatch(() => _viewModel.Begin(action, runId));
                    },
                    Chunk: (runId, chunk) => _dispatch(() =>
                    {
                        if (_viewModel.Append(action, runId, chunk))
                            _viewModel.FlushPendingUpdates();
                    }),
                    ReasoningChunk: (runId, chunk) => _dispatch(() =>
                    {
                        if (_viewModel.AppendReasoning(action, runId, chunk))
                            _viewModel.FlushPendingUpdates();
                    }))).ConfigureAwait(false);

            if (!result.IsObsolete)
            {
                _dispatch(() => _viewModel.Complete(action, result.RunId));
                await RecordHistoryAsync(action, sourceText, result.Output, config).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var message = FriendlyMessage(ex);
            if (startedRunId != 0)
                _dispatch(() => _viewModel.Fail(action, startedRunId, message));
            else
                PresentError(action, message);
        }
    }

    private async Task RecordHistoryAsync(TextAction action, string source, string result, AppConfig config)
    {
        if (_historyStore is null || config.HistoryRetentionDays == 0)
            return;

        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var actionName = action == TextAction.Translate ? "translate" : "rewrite";
        var entry = new HistoryEntry(
            $"{timestampMs}-{actionName}-{Guid.NewGuid():N}",
            timestampMs,
            actionName,
            source,
            result,
            "Completed locally",
            action == TextAction.Translate ? config.TargetLanguage : null);

        try
        {
            await _historyStore.ApplyRetentionAsync(
                config.HistoryRetentionDays,
                timestampMs,
                _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // History is best-effort and must never turn a successful action into an error.
        }

        try
        {
            await _historyStore.AppendAsync(entry, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            // History is best-effort and must never turn a successful action into an error.
        }
    }

    private void PresentError(TextAction action, string message)
    {
        var runId = Interlocked.Decrement(ref _presentationErrorRunId);
        _dispatch(() =>
        {
            _viewModel.Begin(action, runId);
            _viewModel.Fail(action, runId, message);
        });
    }

    private static string FriendlyMessage(Exception exception)
    {
        if (exception is InvalidOperationException &&
            exception.Message.Contains("model", StringComparison.OrdinalIgnoreCase) &&
            exception.Message.Contains("loaded", StringComparison.OrdinalIgnoreCase))
        {
            return "Local model is not loaded. Open Model settings to continue.";
        }

        return $"Local inference failed: {exception.Message}";
    }

    private void Track(Task task)
    {
        lock (_tasksLock)
            _tasks.Add(task);

        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
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
        _viewModel.RerunRequested -= OnRerunRequested;
        _viewModel.TranslateLanguageRequested -= OnTranslateLanguageRequested;
        _viewModel.CopyRequested -= OnCopyRequested;
        _viewModel.ReplaceRequested -= OnReplaceRequested;
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
