using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Butchi.App.Styling;
using Butchi.Core.Configuration;
using Butchi.Inference;

namespace Butchi.App.Models;

public sealed class ModelManagementView : UserControl
{
    private readonly ModelManagementViewModel _viewModel;
    private readonly StackPanel _statusPanel;
    private readonly TextBlock _saveStatus;
    private readonly ComboBox _modelPicker;

    public ModelManagementView(ModelManagementViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        _statusPanel = new StackPanel { Spacing = 8 };
        _saveStatus = new TextBlock { FontSize = 11, Opacity = 0.68 };
        _modelPicker = new ComboBox
        {
            ItemsSource = viewModel.Catalog,
            SelectedItem = viewModel.SelectedModel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 38
        };
        _modelPicker.SelectionChanged += (_, _) =>
        {
            if (_modelPicker.SelectedItem is ModelOption model)
                _viewModel.SelectModel(model);
        };

        var content = new StackPanel
        {
            Margin = new Thickness(36, 30, 42, 48),
            Spacing = 20,
            MaxWidth = 860,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        content.Children.Add(new TextBlock
        {
            Text = "MODEL",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = ButchiTheme.CobaltBrush,
            LetterSpacing = 1.2
        });
        content.Children.Add(new TextBlock
        {
            Text = "Local model",
            FontSize = 30,
            FontWeight = FontWeight.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "Butchi keeps the selected GGUF model ready automatically. Your text stays on this device.",
            FontSize = 14,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(Card(BuildModelSection()));
        content.Children.Add(Card(BuildDeviceSection()));
        content.Children.Add(BuildAdvancedSection());
        content.Children.Add(_saveStatus);

        Content = new ScrollViewer { Content = content };
        _viewModel.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
        Refresh();
        _viewModel.EnsureSelectedModelReady();
    }

    private Control BuildModelSection()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(SectionTitle("Model setup"));
        panel.Children.Add(new TextBlock
        {
            Text = "Recommended",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = ButchiTheme.CobaltBrush
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Qwen 3.5 0.8B · Q4_K_M",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "A compact default for fast Translate and Rewrite on Windows laptops.",
            FontSize = 13,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(_modelPicker);
        panel.Children.Add(_statusPanel);
        return panel;
    }

    private Control BuildDeviceSection()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(SectionTitle("Device"));
        panel.Children.Add(new TextBlock
        {
            Text = "Choose how Butchi selects the llama.cpp backend. Auto prefers an available GPU and falls back to CPU.",
            FontSize = 13,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        });

        var picker = new ComboBox
        {
            ItemsSource = Enum.GetValues<BackendPreference>(),
            SelectedItem = _viewModel.BackendPreference,
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        picker.SelectionChanged += async (_, _) =>
        {
            if (picker.SelectedItem is BackendPreference preference)
            {
                await _viewModel.SetBackendPreferenceAsync(preference, CancellationToken.None);
                Refresh();
            }
        };
        panel.Children.Add(picker);
        return panel;
    }

    private Control BuildAdvancedSection()
    {
        var advanced = new StackPanel { Spacing = 12 };
        advanced.Children.Add(NumberField("Max output tokens", _viewModel.MaxTokens.ToString(), async text =>
        {
            if (uint.TryParse(text, out var value))
                await _viewModel.SetMaxTokensAsync(value, CancellationToken.None);
        }));
        advanced.Children.Add(NumberField("Temperature", _viewModel.Temperature.ToString("0.00"), async text =>
        {
            if (float.TryParse(text, out var value))
                await _viewModel.SetTemperatureAsync(value, CancellationToken.None);
        }));
        advanced.Children.Add(NumberField("GPU layers", _viewModel.GpuLayers.ToString(), async text =>
        {
            if (uint.TryParse(text, out var value))
                await _viewModel.SetGpuLayersAsync(value, CancellationToken.None);
        }));

        return new Expander
        {
            Header = "Advanced inference settings",
            IsExpanded = false,
            Content = Card(advanced),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private Control NumberField(string label, string value, Func<string, Task> save)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,180") };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        });
        var input = new TextBox { Text = value, MinHeight = 36 };
        input.SetValue(Grid.ColumnProperty, 1);
        input.LostFocus += async (_, _) =>
        {
            await save(input.Text ?? string.Empty);
            Refresh();
        };
        grid.Children.Add(input);
        return grid;
    }

    private void Refresh()
    {
        _statusPanel.Children.Clear();

        switch (_viewModel.LifecycleState)
        {
            case ModelLifecycleState.Checking:
                AddStatus("Checking model", ButchiTheme.Cobalt, "Checking the selected model on this device…");
                break;
            case ModelLifecycleState.Downloading:
                AddDownloadStatus();
                break;
            case ModelLifecycleState.Loading:
                AddStatus("Loading model", ButchiTheme.Cobalt, "Preparing the selected model for local inference…");
                break;
            case ModelLifecycleState.Error:
                AddStatus(
                    "Model error",
                    ButchiTheme.Warning,
                    string.IsNullOrWhiteSpace(_viewModel.LifecycleError)
                        ? "The selected model could not be prepared. Select it again to retry."
                        : $"{_viewModel.LifecycleError} Select the model again to retry.");
                break;
            default:
                if (_viewModel.IsLoaded)
                {
                    _statusPanel.Children.Add(StatePill("Ready", ButchiTheme.Success));
                    _statusPanel.Children.Add(new TextBlock
                    {
                        Text = $"Active backend: {_viewModel.ActualBackend ?? "Auto"} · Device: {_viewModel.ActualDevice ?? "Local device"}",
                        FontSize = 12,
                        Opacity = 0.76,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    AddStatus("Preparing model", ButchiTheme.Cobalt, "The selected model will be fetched if needed and prepared automatically.");
                }
                break;
        }

        _saveStatus.Text = _viewModel.SaveStatus;
    }

    private void AddDownloadStatus()
    {
        _statusPanel.Children.Add(StatePill("Downloading", ButchiTheme.Cobalt));
        var progress = _viewModel.DownloadProgress;
        var fraction = progress?.Fraction;
        _statusPanel.Children.Add(new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = fraction ?? 0,
            IsIndeterminate = fraction is null,
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        var text = progress is null
            ? "Starting background transfer…"
            : FormatDownloadProgress(progress);
        _statusPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            Opacity = 0.76,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void AddStatus(string title, Color color, string description)
    {
        _statusPanel.Children.Add(StatePill(title, color));
        _statusPanel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private static string FormatDownloadProgress(ModelDownloadProgress progress)
    {
        var downloaded = FormatBytes(progress.BytesDownloaded);
        if (progress.TotalBytes is not > 0)
            return $"Downloading · {downloaded}";

        var percent = progress.Fraction is { } fraction ? $"{fraction:P0}" : "—";
        return $"Downloading {percent} · {downloaded} / {FormatBytes(progress.TotalBytes.Value)}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        double display = value;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        return unit == 0 ? $"{display:0} {units[unit]}" : $"{display:0.0} {units[unit]}";
    }

    private static Border StatePill(string text, Color color) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(10, 5),
        CornerRadius = new CornerRadius(999),
        Background = new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B)),
        Child = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            FontWeight = FontWeight.SemiBold,
            FontSize = 11
        }
    };

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeight.SemiBold
    };

    private static Border Card(Control child) => new()
    {
        Padding = new Thickness(20),
        CornerRadius = new CornerRadius(14),
        BorderThickness = new Thickness(1),
        BorderBrush = ButchiTheme.DividerBrush,
        Background = ButchiTheme.SubtleSurfaceBrush,
        Child = child
    };
}
