using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butchi.App.Branding;
using Butchi.App.Styling;
using Butchi.Core.Configuration;
using Butchi.Inference;

namespace Butchi.App.Startup;

public interface IWelcomeSetupSurface
{
    event Action<WelcomeSetupCompletion>? Completed;
    event Action? ExitRequested;
    void Show();
    void CloseAfterCompletion();
}

public interface ICancellableWelcomeSetupSurface
{
    void SetOperationCancellation(CancellationToken cancellationToken);
    void CancelActiveOperation();
    ValueTask WaitForActiveOperationAsync();
}

public interface IWelcomeSetupHost
{
    ValueTask<WelcomeSetupCompletion?> ShowAsync(
        WelcomeSetupViewModel viewModel,
        CancellationToken cancellationToken);
}

public sealed class WelcomeSetupHost(
    Func<WelcomeSetupViewModel, IWelcomeSetupSurface>? createSurface = null) : IWelcomeSetupHost
{
    private readonly Func<WelcomeSetupViewModel, IWelcomeSetupSurface> _createSurface =
        createSurface ?? (viewModel => new WelcomeSetupWindow(viewModel));

    public async ValueTask<WelcomeSetupCompletion?> ShowAsync(
        WelcomeSetupViewModel viewModel,
        CancellationToken cancellationToken)
    {
        var surface = _createSurface(viewModel);
        var cancellableSurface = surface as ICancellableWelcomeSetupSurface;
        cancellableSurface?.SetOperationCancellation(cancellationToken);
        var completion = new TaskCompletionSource<WelcomeSetupCompletion?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete(WelcomeSetupCompletion value) => completion.TrySetResult(value);
        void Exit()
        {
            cancellableSurface?.CancelActiveOperation();
            completion.TrySetResult(null);
        }

        surface.Completed += Complete;
        surface.ExitRequested += Exit;
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        surface.Show();
        try
        {
            return await completion.Task;
        }
        finally
        {
            cancellableSurface?.CancelActiveOperation();
            if (cancellableSurface is not null)
                await cancellableSurface.WaitForActiveOperationAsync();
            surface.Completed -= Complete;
            surface.ExitRequested -= Exit;
            surface.CloseAfterCompletion();
        }
    }
}

public sealed class WelcomeSetupWindow : Window, IWelcomeSetupSurface, ICancellableWelcomeSetupSurface
{
    private readonly WelcomeSetupViewModel _viewModel;
    private readonly ComboBox _theme;
    private readonly TextBox _targetLanguage;
    private readonly ComboBox _resultAction;
    private readonly ComboBox _model;
    private readonly Button _download;
    private readonly ProgressBar _progress;
    private readonly TextBlock _progressText;
    private readonly TextBlock _status;
    private readonly TextBlock _error;
    private readonly Button _finish;
    private CancellationToken _hostCancellationToken;
    private CancellationTokenSource? _operationCancellation;
    private Task _activeOperation = Task.CompletedTask;
    private bool _completed;

    public WelcomeSetupWindow(WelcomeSetupViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = "Welcome to Butchi";
        Icon = BrandAssets.CreateWindowIcon();
        Width = 760;
        Height = 700;
        MinWidth = 640;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _theme = new ComboBox { ItemsSource = Enum.GetValues<AppThemePreference>(), SelectedItem = viewModel.Theme };
        _targetLanguage = new TextBox { Text = viewModel.TargetLanguage, PlaceholderText = "Target language" };
        _resultAction = new ComboBox { ItemsSource = Enum.GetValues<ResultAction>(), SelectedItem = viewModel.ResultAction };
        _model = new ComboBox { ItemsSource = viewModel.Catalog, SelectedItem = viewModel.SelectedModel };
        _download = new Button { HorizontalAlignment = HorizontalAlignment.Right };
        _progress = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false };
        _progressText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            IsVisible = false
        };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _error = new TextBlock
        {
            Foreground = new SolidColorBrush(ButchiTheme.Error),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        _finish = new Button { HorizontalAlignment = HorizontalAlignment.Right };

        _theme.SelectionChanged += (_, _) =>
        {
            if (_theme.SelectedItem is AppThemePreference value) _viewModel.Theme = value;
        };
        _targetLanguage.TextChanged += (_, _) => _viewModel.TargetLanguage = _targetLanguage.Text ?? string.Empty;
        _resultAction.SelectionChanged += (_, _) =>
        {
            if (_resultAction.SelectedItem is ResultAction value) _viewModel.ResultAction = value;
        };
        _model.SelectionChanged += (_, _) =>
        {
            if (_model.SelectedItem is ModelOption value) _viewModel.SelectModel(value);
        };
        _download.Click += (_, _) =>
        {
            if (_viewModel.IsBusy && _viewModel.Stage == WelcomeSetupStage.Downloading)
            {
                CancelActiveOperation();
                return;
            }

            _activeOperation = DownloadModelAsync();
        };

        var exit = new Button { Content = "Exit" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        _finish.Click += (_, _) =>
        {
            if (_viewModel.IsBusy && _viewModel.Stage == WelcomeSetupStage.Loading)
            {
                CancelActiveOperation();
                return;
            }

            _activeOperation = FinishSetupAsync();
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { exit, _finish }
        };

        var content = new StackPanel
        {
            Spacing = 18,
            Margin = new Thickness(36),
            Children =
            {
                BuildHeader(),
                BuildSettingsCard(),
                BuildModelCard(),
                _status,
                _error,
                actions
            }
        };
        Content = new ScrollViewer { Content = content };
        _viewModel.PropertyChanged += ViewModelChanged;
        Closing += (_, _) =>
        {
            if (!_completed) ExitRequested?.Invoke();
        };
        Closed += (_, _) => _viewModel.PropertyChanged -= ViewModelChanged;
        Refresh();
    }

    public event Action<WelcomeSetupCompletion>? Completed;
    public event Action? ExitRequested;

    void IWelcomeSetupSurface.Show() => Show();

    public void CloseAfterCompletion()
    {
        _completed = true;
        if (IsVisible) Close();
    }

    public void SetOperationCancellation(CancellationToken cancellationToken)
    {
        _hostCancellationToken = cancellationToken;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    public void CancelActiveOperation() => _operationCancellation?.Cancel();

    public async ValueTask WaitForActiveOperationAsync()
    {
        try
        {
            await _activeOperation;
        }
        catch (OperationCanceledException) when (_operationCancellation?.IsCancellationRequested == true)
        {
        }
        finally
        {
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }
    }

    private async Task DownloadModelAsync()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_hostCancellationToken);
        var operationCancellation = _operationCancellation;

        try
        {
            await _viewModel.DownloadSelectedModelAsync(operationCancellation.Token);
            Refresh();
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            Refresh();
        }
    }

    private async Task FinishSetupAsync()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_hostCancellationToken);
        var operationCancellation = _operationCancellation;

        try
        {
            var result = await _viewModel.FinishAsync(operationCancellation.Token);
            Refresh();
            if (result is not null) Completed?.Invoke(result);
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            Refresh();
        }
    }

    private Control BuildHeader()
    {
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        brand.Children.Add(new Image { Source = BrandAssets.CreateBitmap(), Width = 54, Height = 54 });
        brand.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Welcome to Butchi", FontSize = 28, FontWeight = FontWeight.Bold },
                new TextBlock
                {
                    Text = "Private local translation and rewriting. Complete settings and prepare one local model.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.72
                }
            }
        });
        return brand;
    }

    private Control BuildSettingsCard()
    {
        var fields = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 12
        };
        fields.Children.Add(Labeled("Theme", _theme, 0));
        fields.Children.Add(Labeled("Target language", _targetLanguage, 1));
        fields.Children.Add(Labeled("After action", _resultAction, 2));
        return Card("Settings", "Confirm the defaults used by Translate and Rewrite.", fields);
    }

    private Control BuildModelCard()
    {
        var picker = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        _model.SetValue(Grid.ColumnProperty, 0);
        _download.SetValue(Grid.ColumnProperty, 1);
        picker.Children.Add(_model);
        picker.Children.Add(_download);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(picker);
        panel.Children.Add(new TextBlock
        {
            Text = "Models run on this device. Download the selected model, then finish setup.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7
        });
        panel.Children.Add(_progress);
        panel.Children.Add(_progressText);
        return Card("Local model", "A loaded model is required before Butchi can start in the tray.", panel);
    }

    private static Control Labeled(string label, Control control, int column)
    {
        var panel = new StackPanel
        {
            Spacing = 6,
            Children = { new TextBlock { Text = label, FontWeight = FontWeight.SemiBold }, control }
        };
        panel.SetValue(Grid.ColumnProperty, column);
        return panel;
    }

    private static Control Card(string title, string subtitle, Control content) =>
        new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderBrush = ButchiTheme.DividerBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                    content
                }
            }
        };

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var isBusy = _viewModel.IsBusy;
        var progress = _viewModel.DownloadProgress;
        var isDownloading = _viewModel.Stage == WelcomeSetupStage.Downloading;
        var isLoading = _viewModel.Stage == WelcomeSetupStage.Loading;
        var showProgress = isDownloading && progress is not null;

        _status.Text = _viewModel.StatusText;
        _error.Text = _viewModel.ErrorMessage;
        _error.IsVisible = !string.IsNullOrWhiteSpace(_viewModel.ErrorMessage);

        _theme.IsEnabled = !isBusy;
        _targetLanguage.IsEnabled = !isBusy;
        _resultAction.IsEnabled = !isBusy;
        _model.IsEnabled = !isBusy;

        _download.Content = _viewModel.DownloadActionText;
        _download.IsEnabled = _viewModel.CanDownloadModel || (isBusy && isDownloading);
        _download.IsVisible = _viewModel.CanDownloadModel || (isBusy && isDownloading);

        _finish.Content = _viewModel.PrimaryActionText;
        _finish.IsEnabled = _viewModel.CanFinish || (isBusy && isLoading);

        _progress.IsVisible = showProgress;
        _progress.IsIndeterminate = showProgress && progress?.TotalBytes is not > 0;
        _progress.Value = progress?.Fraction ?? 0;
        _progressText.Text = _viewModel.DownloadProgressText;
        _progressText.IsVisible = showProgress;
    }
}
