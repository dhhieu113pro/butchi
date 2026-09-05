using Avalonia.Threading;
using Butchi.App.Popover;
using Butchi.App.Settings;
using Butchi.Core.Vision;

namespace Butchi.App.Vision;

public sealed class VisionController : IAsyncDisposable
{
    private readonly VisionViewModel _viewModel;
    private readonly IVisionInferenceEngine _inferenceEngine;
    private readonly IScreenCaptureService _captureService;
    private readonly IAppConfigStore _configStore;
    private readonly PopoverWindow _popover;
    private readonly VisionPopoverHost _popoverHost;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _tasksLock = new();
    private readonly HashSet<Task> _tasks = [];
    private VisionCaptureWindow? _activeSelector;
    private int _disposed;

    public VisionController(
        VisionViewModel viewModel,
        IVisionInferenceEngine inferenceEngine,
        IScreenCaptureService captureService,
        IAppConfigStore configStore,
        PopoverWindow popover)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _inferenceEngine = inferenceEngine ?? throw new ArgumentNullException(nameof(inferenceEngine));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _popover = popover ?? throw new ArgumentNullException(nameof(popover));
        _popoverHost = VisionPopoverHost.Attach(popover, viewModel);

        _viewModel.CaptureRequested += OnCaptureRequested;
        _viewModel.AnalyzeRequested += OnAnalyzeRequested;
    }

    private void OnCaptureRequested(object? sender, EventArgs e) => Track(CaptureAsync());

    private void OnAnalyzeRequested(object? sender, string prompt) => Track(AnalyzeAsync(prompt));

    private async Task CaptureAsync()
    {
        if (_lifetime.IsCancellationRequested)
            return;

        try
        {
            var screenInfo = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var screen = _popover.Screens.ScreenFromWindow(_popover) ?? _popover.Screens.Primary;
                if (screen is null)
                    throw new InvalidOperationException("No screen is available for capture.");
                _popover.HidePersistent();
                return (screen.Bounds, screen.Scaling);
            });

            // Give the compositor one frame to remove Butchi from the pixels we are about to copy.
            await Task.Delay(80, _lifetime.Token).ConfigureAwait(false);
            var bounds = screenInfo.Bounds;
            var frame = await _captureService.CaptureAsync(
                new ScreenCaptureBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                _lifetime.Token).ConfigureAwait(false);

            var captureTask = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var selector = new VisionCaptureWindow(frame, bounds, screenInfo.Scaling);
                _activeSelector = selector;
                return selector.CaptureAsync();
            });
            var image = await captureTask.ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _activeSelector = null;
                if (image is { Length: > 0 })
                    _viewModel.SetScreenshot(image);
                _popover.ShowPersistent();
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _activeSelector = null;
                _viewModel.Fail($"Could not capture the screen: {exception.Message}");
                _popover.ShowPersistent();
            });
        }
    }

    private async Task AnalyzeAsync(string prompt)
    {
        if (_lifetime.IsCancellationRequested)
            return;

        var image = _viewModel.ScreenshotPng;
        if (image is not { Length: > 0 })
        {
            await Dispatcher.UIThread.InvokeAsync(() => _viewModel.Fail("Capture a screenshot before running Vision."));
            return;
        }

        try
        {
            var config = await _configStore.LoadAsync(_lifetime.Token).ConfigureAwait(false);
            var request = new VisionInferenceRequest(
                prompt,
                image,
                config.MaxTokens,
                config.Temperature,
                0);

            await foreach (var chunk in _inferenceEngine.GenerateAsync(request, config, _lifetime.Token)
                               .WithCancellation(_lifetime.Token)
                               .ConfigureAwait(false))
            {
                if (chunk.Length == 0)
                    continue;
                await Dispatcher.UIThread.InvokeAsync(() => _viewModel.Append(chunk));
            }

            await Dispatcher.UIThread.InvokeAsync(_viewModel.Complete);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                _viewModel.Fail($"Local vision failed: {exception.Message}"));
        }
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _viewModel.CaptureRequested -= OnCaptureRequested;
        _viewModel.AnalyzeRequested -= OnAnalyzeRequested;
        _lifetime.Cancel();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _activeSelector?.Close();
            _activeSelector = null;
            _popoverHost.Dispose();
        });

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
