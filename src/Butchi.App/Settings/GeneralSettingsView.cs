using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butchi.App.Styling;
using Butchi.Core.Configuration;

namespace Butchi.App.Settings;

public sealed class GeneralSettingsView : UserControl
{
    private static readonly string[] Languages =
    [
        "Vietnamese", "English", "Chinese (Simplified)", "Chinese (Traditional)",
        "Japanese", "Korean", "French", "German", "Spanish", "Portuguese", "Thai", "Indonesian"
    ];

    private readonly GeneralSettingsViewModel _viewModel;
    private readonly Action<AppThemePreference> _applyTheme;
    private readonly TextBlock _saveStatus;
    private readonly Dictionary<CheckBox, string> _favoriteLanguages = [];
    private bool _ready;

    public GeneralSettingsView(
        GeneralSettingsViewModel viewModel,
        Action<AppThemePreference> applyTheme)
    {
        _viewModel = viewModel;
        _applyTheme = applyTheme;

        _saveStatus = new TextBlock
        {
            Text = viewModel.SaveStatus,
            FontSize = 12,
            Opacity = 0.72,
            VerticalAlignment = VerticalAlignment.Center
        };

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = BuildContent()
        };
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GeneralSettingsViewModel.SaveStatus))
                _saveStatus.Text = _viewModel.SaveStatus;
        };
        _ready = true;
    }

    private Control BuildContent()
    {
        var content = new StackPanel
        {
            Margin = new Thickness(36, 30, 42, 48),
            Spacing = 22,
            MaxWidth = 900,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        content.Children.Add(BuildHeader());
        content.Children.Add(BuildAppearanceCard());
        content.Children.Add(BuildPopoverCard());
        content.Children.Add(BuildActionsCard());
        return content;
    }

    private Control BuildHeader()
    {
        var titleStack = new StackPanel { Spacing = 5 };
        titleStack.Children.Add(new TextBlock
        {
            Text = "GENERAL",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = ButchiTheme.CobaltBrush
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Everyday preferences",
            FontSize = 30,
            FontWeight = FontWeight.SemiBold
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Tune how Butchi looks and what happens when Translate or Rewrite completes.",
            FontSize = 14,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        });

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(titleStack);
        _saveStatus.SetValue(Grid.ColumnProperty, 1);
        header.Children.Add(_saveStatus);
        return header;
    }

    private Control BuildAppearanceCard()
    {
        var theme = new ComboBox
        {
            ItemsSource = Enum.GetValues<AppThemePreference>(),
            SelectedItem = _viewModel.Theme,
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        theme.SelectionChanged += async (_, _) =>
        {
            if (!_ready || theme.SelectedItem is not AppThemePreference selected)
                return;
            await RunAsync(() => _viewModel.SetThemeAsync(selected, CancellationToken.None));
            _applyTheme(selected);
        };

        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(SectionHeading("Appearance", "Choose a fixed theme or follow Windows automatically."));
        body.Children.Add(Field("Theme", theme));
        body.Children.Add(Hint("System follows your Windows light/dark preference."));
        return Card(body);
    }

    private Control BuildPopoverCard()
    {
        var hideSeconds = new NumericUpDown
        {
            Minimum = 2,
            Maximum = 30,
            Increment = 1,
            Value = _viewModel.PopoverHideSeconds,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        hideSeconds.ValueChanged += async (_, _) =>
        {
            if (!_ready || hideSeconds.Value is not decimal value)
                return;
            await RunAsync(() => _viewModel.SetPopoverHideSecondsAsync((uint)value, CancellationToken.None));
        };

        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(SectionHeading("Popover", "Choose how long the result stays visible when idle."));
        body.Children.Add(Field("Auto-close after (seconds)", hideSeconds));
        body.Children.Add(Hint("2–30 seconds after the result completes or the pointer leaves. Escape closes immediately."));
        return Card(body);
    }

    private Control BuildActionsCard()
    {
        var translate = new ToggleSwitch
        {
            IsChecked = _viewModel.TranslateEnabled,
            OnContent = "On",
            OffContent = "Off"
        };
        translate.PropertyChanged += async (_, args) =>
        {
            if (_ready && args.Property == ToggleSwitch.IsCheckedProperty)
                await RunAsync(() => _viewModel.SetTranslateEnabledAsync(translate.IsChecked == true, CancellationToken.None));
        };

        var rewrite = new ToggleSwitch
        {
            IsChecked = _viewModel.RewriteEnabled,
            OnContent = "On",
            OffContent = "Off"
        };
        rewrite.PropertyChanged += async (_, args) =>
        {
            if (_ready && args.Property == ToggleSwitch.IsCheckedProperty)
                await RunAsync(() => _viewModel.SetRewriteEnabledAsync(rewrite.IsChecked == true, CancellationToken.None));
        };

        var targetLanguage = new ComboBox
        {
            ItemsSource = Languages,
            SelectedItem = _viewModel.TargetLanguage,
            MinWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        targetLanguage.SelectionChanged += async (_, _) =>
        {
            if (!_ready || targetLanguage.SelectedItem is not string selected)
                return;
            await RunAsync(() => _viewModel.SetTargetLanguageAsync(selected, CancellationToken.None));
        };

        var favoritePanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var language in Languages)
        {
            var check = new CheckBox
            {
                Content = language,
                IsChecked = _viewModel.FavoriteLanguages.Contains(language, StringComparer.OrdinalIgnoreCase),
                Margin = new Thickness(0, 0, 18, 8)
            };
            check.PropertyChanged += FavoriteLanguageChanged;
            _favoriteLanguages[check] = language;
            favoritePanel.Children.Add(check);
        }

        var resultAction = new ComboBox
        {
            ItemsSource = Enum.GetValues<ResultAction>(),
            SelectedItem = _viewModel.ResultAction,
            MinWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        resultAction.SelectionChanged += async (_, _) =>
        {
            if (!_ready || resultAction.SelectedItem is not ResultAction selected)
                return;
            await RunAsync(() => _viewModel.SetResultActionAsync(selected, CancellationToken.None));
        };

        var body = new StackPanel { Spacing = 18 };
        body.Children.Add(SectionHeading("Actions", "Control the two local AI actions and their default result behavior."));
        body.Children.Add(RowField("Enable Translate", "Show translation alongside Rewrite.", translate));
        body.Children.Add(RowField("Enable Rewrite", "Keep the writing-assistant action available.", rewrite));
        body.Children.Add(Field("Target language", targetLanguage));
        body.Children.Add(Field("Favorite target languages", favoritePanel));
        body.Children.Add(Hint("Choose up to 5. These become fast target-language choices in the Translate popover."));
        body.Children.Add(Field("After an explicit action", resultAction));
        return Card(body);
    }

    private async void FavoriteLanguageChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_ready || e.Property != CheckBox.IsCheckedProperty)
            return;

        var selected = _favoriteLanguages
            .Where(pair => pair.Key.IsChecked == true)
            .Select(pair => pair.Value)
            .ToArray();

        if (selected.Length > 5)
        {
            if (sender is CheckBox changed)
                changed.IsChecked = false;
            return;
        }

        await RunAsync(() => _viewModel.SetFavoriteLanguagesAsync(selected, CancellationToken.None));
    }

    private static Border Card(Control child) => new()
    {
        Child = child,
        Padding = new Thickness(22),
        CornerRadius = new CornerRadius(14),
        BorderThickness = new Thickness(1),
        BorderBrush = ButchiTheme.DividerBrush,
        Background = ButchiTheme.SubtleSurfaceBrush
    };

    private static Control SectionHeading(string title, string subtitle)
    {
        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 19,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 13,
            Opacity = 0.68,
            TextWrapping = TextWrapping.Wrap
        });
        return stack;
    }

    private static Control Field(string label, Control control)
    {
        var stack = new StackPanel { Spacing = 7 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(control);
        return stack;
    }

    private static Control RowField(string label, string description, Control control)
    {
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
        text.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.68 });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(text);
        control.SetValue(Grid.ColumnProperty, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return grid;
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Opacity = 0.64,
        TextWrapping = TextWrapping.Wrap
    };

    private static async Task RunAsync(Func<ValueTask> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // SaveStatus is set by the view model. Keep the UI responsive and let the status communicate failure.
        }
    }
}
