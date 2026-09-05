using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Butchi.App.Popover;
using Butchi.App.Styling;

namespace Butchi.App.Vision;

public sealed class VisionPopoverHost : IDisposable
{
    private readonly PopoverWindow _window;
    private readonly VisionViewModel _viewModel;
    private readonly Control _textContent;
    private readonly Grid _root;
    private readonly Button _visionButton;
    private readonly Border _visionPanel;
    private readonly Image _preview;
    private readonly TextBox _prompt;
    private readonly Button _analyze;
    private readonly TextBlock _status;
    private readonly TextBlock _result;
    private Bitmap? _previewBitmap;
    private byte[]? _previewBytes;
    private int _disposed;

    private VisionPopoverHost(PopoverWindow window, VisionViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;
        _textContent = window.Content as Control
            ?? throw new InvalidOperationException("Butchi popover content must be an Avalonia control.");

        _preview = new Image
        {
            MaxHeight = 250,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false
        };

        _prompt = new TextBox
        {
            Text = viewModel.Prompt,
            Watermark = "Ask about this screenshot…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 66,
            MaxHeight = 120
        };
        _prompt.TextChanged += (_, _) => _viewModel.SetPrompt(_prompt.Text ?? string.Empty);

        _analyze = new Button
        {
            Content = "Analyze",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 8),
            CornerRadius = new CornerRadius(12)
        };
        _analyze.Click += (_, _) => _viewModel.RequestAnalyze();

        _status = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1,
            Foreground = ButchiTheme.CobaltBrush
        };
        _result = new TextBlock
        {
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };

        _visionPanel = BuildVisionPanel();
        _visionButton = BuildVisionButton();
        _root = new Grid();
        _root.Children.Add(_textContent);
        _root.Children.Add(_visionPanel);
        _root.Children.Add(_visionButton);
        window.Content = _root;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _window.ViewModel.PropertyChanged += OnTextViewModelPropertyChanged;
        Refresh();
    }

    public static VisionPopoverHost Attach(PopoverWindow window, VisionViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(viewModel);
        return new VisionPopoverHost(window, viewModel);
    }

    private Border BuildVisionPanel()
    {
        var content = new StackPanel { Spacing = 12 };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        header.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = "VISION",
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    LetterSpacing = 1,
                    Foreground = ButchiTheme.CobaltBrush
                },
                new TextBlock
                {
                    Text = "LFM2.5-VL · local",
                    FontSize = 11,
                    Opacity = 0.65
                }
            }
        });

        var recapture = new Button
        {
            Content = "Capture again",
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 7, 0)
        };
        recapture.Click += (_, _) => _viewModel.RequestCapture();
        recapture.SetValue(Grid.ColumnProperty, 1);
        header.Children.Add(recapture);

        var back = new Button
        {
            Content = "×",
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(17),
            FontSize = 20
        };
        back.Click += (_, _) => _viewModel.Deactivate();
        back.SetValue(Grid.ColumnProperty, 2);
        header.Children.Add(back);
        content.Children.Add(header);

        content.Children.Add(new Border
        {
            MinHeight = 120,
            MaxHeight = 270,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(14),
            Background = ButchiTheme.CardSurfaceBrush(_window.ActualThemeVariant),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            Child = _preview
        });

        content.Children.Add(_prompt);
        content.Children.Add(_analyze);

        var resultContent = new StackPanel { Spacing = 8 };
        resultContent.Children.Add(_status);
        resultContent.Children.Add(new ScrollViewer
        {
            MaxHeight = 240,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _result
        });
        content.Children.Add(new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(14),
            Background = ButchiTheme.CardSurfaceBrush(_window.ActualThemeVariant),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            Child = resultContent
        });

        return new Border
        {
            IsVisible = false,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(24),
            Background = ButchiTheme.NavigationSurfaceBrush(_window.ActualThemeVariant),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            Child = content
        };
    }

    private Button BuildVisionButton()
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = "▣",
                FontSize = 19,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = 46,
            Height = 46,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            Background = ButchiTheme.CardSurfaceBrush(_window.ActualThemeVariant),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(190, 23, 0, 0)
        };
        ToolTip.SetTip(button, "Vision screenshot");
        button.Click += (_, _) => _viewModel.RequestCapture();
        return button;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    private void OnTextViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_disposed != 0)
            return;

        var active = _viewModel.IsActive;
        _textContent.IsVisible = !active;
        _visionPanel.IsVisible = active;
        _visionButton.IsVisible = !active && !_window.ViewModel.IsCompact;

        if (!string.Equals(_prompt.Text, _viewModel.Prompt, StringComparison.Ordinal))
            _prompt.Text = _viewModel.Prompt;

        UpdatePreview();
        _analyze.IsEnabled = _viewModel.HasScreenshot && !_viewModel.IsRunning && !string.IsNullOrWhiteSpace(_viewModel.Prompt);
        _prompt.IsEnabled = !_viewModel.IsRunning;

        if (_viewModel.IsRunning)
        {
            _status.Text = "WORKING";
            _status.Foreground = ButchiTheme.CobaltBrush;
            _result.Text = string.IsNullOrWhiteSpace(_viewModel.Output)
                ? "Preparing local vision model…"
                : _viewModel.Output + " ▍";
            _result.Foreground = null;
        }
        else if (_viewModel.ErrorMessage is { } error)
        {
            _status.Text = "ERROR";
            _status.Foreground = new SolidColorBrush(ButchiTheme.Error);
            _result.Text = error;
            _result.Foreground = new SolidColorBrush(ButchiTheme.Error);
        }
        else if (!string.IsNullOrWhiteSpace(_viewModel.Output))
        {
            _status.Text = "RESULT";
            _status.Foreground = ButchiTheme.CobaltBrush;
            _result.Text = _viewModel.Output;
            _result.Foreground = null;
        }
        else
        {
            _status.Text = "VISION";
            _status.Foreground = ButchiTheme.CobaltBrush;
            _result.Text = _viewModel.HasScreenshot
                ? "Ask a question about the captured area."
                : "Capture an area of the screen to analyze it locally.";
            _result.Foreground = null;
        }
    }

    private void UpdatePreview()
    {
        var bytes = _viewModel.ScreenshotPng;
        if (bytes is not { Length: > 0 })
        {
            _preview.IsVisible = false;
            _preview.Source = null;
            return;
        }

        if (!ReferenceEquals(bytes, _previewBytes) || _previewBitmap is null)
        {
            _previewBitmap?.Dispose();
            using var stream = new MemoryStream(bytes, writable: false);
            _previewBitmap = new Bitmap(stream);
            _previewBytes = bytes;
        }

        _preview.Source = _previewBitmap;
        _preview.IsVisible = true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _window.ViewModel.PropertyChanged -= OnTextViewModelPropertyChanged;
        _preview.Source = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _previewBytes = null;
    }
}
