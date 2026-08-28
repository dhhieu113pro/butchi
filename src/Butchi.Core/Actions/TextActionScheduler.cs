using System.Text;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Core.Prompts;

namespace Butchi.Core.Actions;

public sealed class TextActionScheduler : IAsyncDisposable
{
    private readonly IInferenceEngine _engine;
    private readonly IResultActionSink _resultSink;
    private readonly SemaphoreSlim _inferenceLane = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<TextAction, long> _runIds = new();
    private readonly Dictionary<TextAction, CancellationTokenSource> _activeRuns = new();
    private bool _disposed;

    public TextActionScheduler(IInferenceEngine engine, IResultActionSink resultSink)
    {
        _engine = engine;
        _resultSink = resultSink;
    }

    public async Task<TextActionRunResult> RunAsync(
        TextAction action,
        string input,
        AppConfig config,
        InputOrigin origin,
        CancellationToken cancellationToken,
        TextActionRunCallbacks? callbacks = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        CancellationTokenSource linkedCts;
        CancellationTokenSource? previous;
        long runId;

        lock (_stateLock)
        {
            _activeRuns.TryGetValue(action, out previous);

            runId = _runIds.TryGetValue(action, out var current) ? current + 1 : 1;
            _runIds[action] = runId;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeRuns[action] = linkedCts;
        }

        // Publish the newer run ID before cancellation so the stale run can
        // reliably recognize that its cancellation is internal/obsolete.
        previous?.Cancel();

        try
        {
            await _inferenceLane.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                if (IsObsolete(action, runId))
                {
                    return new TextActionRunResult(action, runId, string.Empty, true);
                }

                callbacks?.Started?.Invoke(runId);

                var prompt = PromptBuilder.Build(action, input, config);
                var request = new InferenceRequest(prompt, config.MaxTokens, config.Temperature, 0);
                var output = new StringBuilder();

                try
                {
                    await foreach (var chunk in _engine.GenerateAsync(request, linkedCts.Token).ConfigureAwait(false))
                    {
                        if (IsObsolete(action, runId))
                        {
                            return new TextActionRunResult(action, runId, output.ToString(), true);
                        }

                        callbacks?.Chunk?.Invoke(runId, chunk);
                        output.Append(chunk);
                    }
                }
                catch (OperationCanceledException) when (IsObsolete(action, runId) && !cancellationToken.IsCancellationRequested)
                {
                    return new TextActionRunResult(action, runId, output.ToString(), true);
                }

                if (IsObsolete(action, runId))
                {
                    return new TextActionRunResult(action, runId, output.ToString(), true);
                }

                var result = new TextActionRunResult(action, runId, output.ToString(), false);
                await ApplyAutomaticResultAsync(result.Output, config, origin, linkedCts.Token).ConfigureAwait(false);
                return result;
            }
            finally
            {
                _inferenceLane.Release();
            }
        }
        catch (OperationCanceledException) when (IsObsolete(action, runId) && !cancellationToken.IsCancellationRequested)
        {
            return new TextActionRunResult(action, runId, string.Empty, true);
        }
        finally
        {
            lock (_stateLock)
            {
                if (_activeRuns.TryGetValue(action, out var current) && ReferenceEquals(current, linkedCts))
                {
                    _activeRuns.Remove(action);
                }
            }

            linkedCts.Dispose();
        }
    }

    private bool IsObsolete(TextAction action, long runId)
    {
        lock (_stateLock)
        {
            return !_runIds.TryGetValue(action, out var current) || current != runId;
        }
    }

    private async Task ApplyAutomaticResultAsync(
        string output,
        AppConfig config,
        InputOrigin origin,
        CancellationToken cancellationToken)
    {
        if (!AutomationRules.ShouldApplyAutomaticResult(config))
        {
            return;
        }

        switch (config.ResultAction)
        {
            case ResultAction.Copy when origin == InputOrigin.Selection:
                await _resultSink.CopyAsync(output, cancellationToken).ConfigureAwait(false);
                break;
            case ResultAction.Replace when origin == InputOrigin.Selection:
                await _resultSink.ReplaceAsync(output, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        lock (_stateLock)
        {
            foreach (var cts in _activeRuns.Values)
            {
                cts.Cancel();
            }

            _activeRuns.Clear();
        }

        _inferenceLane.Dispose();
        return ValueTask.CompletedTask;
    }
}
