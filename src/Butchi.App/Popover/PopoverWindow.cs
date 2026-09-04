using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Butchi.App.Branding;
using Butchi.App.Styling;
using Butchi.App.Windows;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;

namespace Butchi.App.Popover;

public sealed class PopoverWindow : Window, IWindowsPopoverView
{
    private readonly PopoverWindowController _controller;

    public PopoverWindow(PopoverViewModel viewModel, PopoverWindowController? controller = null)
    {
        ViewModel = viewModel;
        _controller = controller ?? new PopoverWindowController();
        DataContext = viewModel;

        var profile = PopoverWindowProfile.Default;
        WindowDecorations = profile.Borderless ? WindowDecorations.None : WindowDecorations.Full;
        Topmost = profile.Topmost;
        ShowInTaskbar = profile.ShowInTaskbar;
        CanResize = profile.CanResize;
        Icon = BrandAssets.CreateWindowIcon();
        Width = 460;
        MinHeight = 260;
        MaxHeight = 720;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;

        RefreshContent();
        ViewModel.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(RefreshContent);
        ActualThemeVariantChanged += (_, _) => RefreshContent();
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        KeyDown += OnKeyDown;
        Closing += OnClosing;
    }

    public PopoverViewModel ViewModel { get; }
    public Guid InstanceId => _controller.InstanceId;

    public void ShowPersistent()
    {
        _controller.Show();
        if (!IsVisible) Show(); else Activate();
    }

    void IWindowsPopoverView.SetSelectionInput(string input, AppConfig config) =>
        Dispatcher.UIThread.Post(() =>
        {
            var automaticAction = ViewModel.SetSession(input, config);
            if (automaticAction is { } action)
                ViewModel.SelectAction(action);
        });

    void IWindowsPopoverView.SetPosition(double x, double y) =>
        Dispatcher.UIThread.Post(() => Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y)));

    void IWindowsPopoverView.ShowPersistent() => Dispatcher.UIThread.Post(ShowPersistent);

    public void HidePersistent()
    {
        _controller.Hide();
        Hide();
    }

    public void ApplyTheme(PopoverTheme theme)
    {
        RequestedThemeVariant = PopoverThemePolicy.ToVariantName(theme) switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void RefreshContent()
    {
        var selected = ViewModel.SelectedState;
        var root = new StackPanel { Spacing = 12 };

        var brand = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        brand.Children.Add(new Image { Source = BrandAssets.CreateBitmap(), Width = 28, Height = 28, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
        var title = new StackPanel { Spacing = 0, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "Butchi", FontSize = 16, FontWeight = FontWeight.Bold });
        title.Children.Add(new TextBlock { Text = "Local AI", FontSize = 10, Opacity = 0.6 });
        title.SetValue(Grid.ColumnProperty, 1);
        brand.Children.Add(title);
        var privatePill = new Border
        {
            Padding = new Thickness(9, 4),
            CornerRadius = new CornerRadius(999),
            Background = ButchiTheme.LocalStatusSurfaceBrush(ActualThemeVariant),
            Child = new TextBlock
            {
                Text = "On device",
                FontSize = 10,
                Foreground = ButchiTheme.LocalStatusForegroundBrush(ActualThemeVariant)
            }
        };
        privatePill.SetValue(Grid.ColumnProperty, 2);
        brand.Children.Add(privatePill);
        root.Children.Add(brand);

        var bothActionsEnabled = ViewModel.TranslateEnabled && ViewModel.RewriteEnabled;
        if (ViewModel.TranslateEnabled || ViewModel.RewriteEnabled)
        {
            var selector = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(bothActionsEnabled ? "*,*" : "*")
            };

            if (ViewModel.TranslateEnabled)
                selector.Children.Add(ActionButton("Translate", TextAction.Translate, bothActionsEnabled));

            if (ViewModel.RewriteEnabled)
            {
                var rewrite = ActionButton("Rewrite", TextAction.Rewrite, bothActionsEnabled);
                if (bothActionsEnabled)
                    rewrite.SetValue(Grid.ColumnProperty, 1);
                selector.Children.Add(rewrite);
            }

            root.Children.Add(selector);
        }

        if (!string.IsNullOrWhiteSpace(ViewModel.SourceText))
        {
            var source = new StackPanel { Spacing = 5 };
            source.Children.Add(new TextBlock { Text = "SOURCE", FontSize = 10, FontWeight = FontWeight.Bold, Opacity = 0.55, LetterSpacing = 1 });
            source.Children.Add(new TextBlock { Text = ViewModel.SourceText, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxHeight = 72 });
            root.Children.Add(Card(source));
        }

        if (ViewModel.TranslateEnabled && ViewModel.SelectedAction == TextAction.Translate)
        {
            var language = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            language.Children.Add(new TextBlock { Text = "To", FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
            foreach (var item in new[] { "Vietnamese", "English", "Japanese" })
            {
                var button = new Button { Content = item, Padding = new Thickness(9, 5), CornerRadius = new CornerRadius(8), FontSize = 11 };
                if (string.Equals(ViewModel.TargetLanguage, item, StringComparison.OrdinalIgnoreCase))
                {
                    button.Background = ButchiTheme.CobaltBrush;
                    button.Foreground = ButchiTheme.WhiteBrush;
                }
                button.Click += (_, _) => ViewModel.RequestFavoriteLanguage(item);
                language.Children.Add(button);
            }
            root.Children.Add(language);
        }

        if (!string.IsNullOrWhiteSpace(selected.Reasoning))
        {
            var thinking = new StackPanel { Spacing = 5 };
            var thinkingLabel = selected.IsRunning && string.IsNullOrEmpty(selected.Output) ? "Thinking…" : "Thinking";
            var toggle = new Button
            {
                Content = new TextBlock
                {
                    Text = $"{(selected.IsThinkingExpanded ? "▾" : "▸")} {thinkingLabel}",
                    FontSize = 11,
                    Opacity = 0.6
                },
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            toggle.Click += (_, _) => ViewModel.RequestToggleThinking();
            thinking.Children.Add(toggle);

            if (selected.IsThinkingExpanded)
            {
                thinking.Children.Add(new TextBlock
                {
                    Text = selected.Reasoning,
                    FontSize = 11,
                    Opacity = 0.6,
                    Margin = new Thickness(16, 0, 0, 0),
                    MaxHeight = 140,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            root.Children.Add(thinking);
        }

        var showResult = !selected.IsRunning ||
            !string.IsNullOrEmpty(selected.Output) ||
            string.IsNullOrWhiteSpace(selected.Reasoning);
        if (showResult)
        {
            var result = new StackPanel { Spacing = 7 };
            result.Children.Add(new TextBlock { Text = selected.IsRunning ? "WORKING" : selected.ErrorMessage is null ? "RESULT" : "ERROR", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = selected.ErrorMessage is null ? ButchiTheme.CobaltBrush : new SolidColorBrush(ButchiTheme.Error), LetterSpacing = 1 });
            if (selected.IsRunning)
                result.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(selected.Output) ? "Running locally…" : selected.Output + " ▍", FontSize = 14, TextWrapping = TextWrapping.Wrap });
            else if (selected.ErrorMessage is { } error)
                result.Children.Add(new TextBlock { Text = error, FontSize = 13, Foreground = new SolidColorBrush(ButchiTheme.Error), TextWrapping = TextWrapping.Wrap });
            else if (!string.IsNullOrWhiteSpace(selected.Output))
                result.Children.Add(new TextBlock { Text = selected.Output, FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            else
                result.Children.Add(new TextBlock
                {
                    Text = bothActionsEnabled
                        ? "Select Translate or Rewrite to run on the selected text."
                        : $"Starting {ViewModel.SelectedAction.ToString().ToLowerInvariant()} locally…",
                    FontSize = 12,
                    Opacity = 0.62,
                    TextWrapping = TextWrapping.Wrap
                });
            root.Children.Add(Card(result));
        }

        if (!selected.IsRunning && (!string.IsNullOrWhiteSpace(selected.Output) || selected.ErrorMessage is not null))
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var rerun = SmallButton("Run again"); rerun.Click += (_, _) => ViewModel.RequestRerun(); actions.Children.Add(rerun);
            if (!string.IsNullOrWhiteSpace(selected.Output))
            {
                var copy = SmallButton("Copy"); copy.Click += (_, _) => ViewModel.RequestCopy(); actions.Children.Add(copy);
                var replace = SmallButton("Replace selection"); replace.Click += (_, _) => ViewModel.RequestReplace(); actions.Children.Add(replace);
            }
            root.Children.Add(actions);
        }

        Content = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            Child = new ScrollViewer { Content = root, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }
        };
    }

    private Button ActionButton(string text, TextAction action, bool split)
    {
        var selected = ViewModel.SelectedAction == action;
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = split
                ? action == TextAction.Translate ? new Thickness(0, 0, 4, 0) : new Thickness(4, 0, 0, 0)
                : new Thickness(0),
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(9),
            FontWeight = FontWeight.SemiBold,
            Background = selected ? ButchiTheme.CobaltBrush : ButchiTheme.CardSurfaceBrush(ActualThemeVariant)
        };
        if (selected) button.Foreground = ButchiTheme.WhiteBrush;
        button.Click += (_, _) => ViewModel.SelectAction(action);
        return button;
    }

    private Border Card(Control child) => new()
    {
        Padding = new Thickness(13),
        CornerRadius = new CornerRadius(11),
        Background = ButchiTheme.CardSurfaceBrush(ActualThemeVariant),
        BorderThickness = new Thickness(1),
        BorderBrush = ButchiTheme.DividerBrush,
        Child = child
    };

    private static Button SmallButton(string text) => new() { Content = text, Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(8), FontSize = 11 };

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _controller.HandlePointerEntered();
    }

    private async void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (await _controller.HandlePointerExitedAsync())
            Dispatcher.UIThread.Post(Hide);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _controller.HandleEscape();
        Hide();
        e.Handled = true;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_controller.IsDisposed) return;
        e.Cancel = true;
        HidePersistent();
    }

    public void Destroy()
    {
        if (_controller.IsDisposed) return;
        _controller.Dispose();
        Closing -= OnClosing;
        Close();
    }
}
