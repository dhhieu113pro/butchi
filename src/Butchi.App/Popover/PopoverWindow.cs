using Avalonia;
using Avalonia.Animation;
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
    public const double CompactWidth = 420;
    public const double ExpandedWidth = 760;
    private const double ResultScrollMaxHeight = 340;

    private readonly PopoverWindowController _controller;
    private readonly TransitioningContentControl _islandHost = new();
    private readonly ContentControl _expandedHost = new();
    private bool? _lastCompactState;
    private double? _anchorCenterX;
    private double _anchorTopY;

    public PopoverWindow(PopoverViewModel viewModel, PopoverWindowController? controller = null)
    {
        ViewModel = viewModel;
        _controller = controller ?? new PopoverWindowController();
        DataContext = viewModel;

        var profile = PopoverWindowProfile.Default;
        WindowDecorations = profile.Borderless ? WindowDecorations.None : WindowDecorations.Full;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = profile.Topmost;
        ShowInTaskbar = profile.ShowInTaskbar;
        CanResize = profile.CanResize;
        Icon = BrandAssets.CreateWindowIcon();
        Width = ExpandedWidth;
        MinHeight = 0;
        MaxHeight = 720;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _islandHost;

        RefreshContent();
        ViewModel.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(RefreshContent);
        ViewModel.ActionStarted += OnActionStarted;
        ViewModel.ActionFinished += OnActionFinished;
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
        Dispatcher.UIThread.Post(() =>
        {
            _anchorCenterX = x + (ExpandedWidth / 2);
            _anchorTopY = y;
            ApplyPresentationGeometry();
        });

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
        var compact = ViewModel.IsCompact;
        var stateChanged = _lastCompactState.HasValue && compact != _lastCompactState;
        _islandHost.PageTransition = stateChanged
            ? new CrossFade(TimeSpan.FromMilliseconds(180))
            : null;

        ApplyPresentationGeometry();

        if (compact)
        {
            _islandHost.Content = BuildCompactIsland();
        }
        else
        {
            _expandedHost.Content = BuildExpandedIsland();
            if (!ReferenceEquals(_islandHost.Content, _expandedHost))
                _islandHost.Content = _expandedHost;
        }

        _lastCompactState = compact;
    }

    private void ApplyPresentationGeometry()
    {
        var targetWidth = ViewModel.IsCompact ? CompactWidth : ExpandedWidth;
        Width = targetWidth;

        if (_anchorCenterX is not { } centerX)
            return;

        Position = new PixelPoint(
            (int)Math.Round(PopoverGeometry.CenteredX(centerX, targetWidth)),
            (int)Math.Round(_anchorTopY));
    }

    private Control BuildCompactIsland()
    {
        var status = ViewModel.SelectedAction == TextAction.Translate
            ? "Translating…"
            : "Rewriting…";

        var island = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        island.Children.Add(new Image
        {
            Source = BrandAssets.CreateBitmap(),
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        });

        var activity = new StackPanel
        {
            Spacing = 1,
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        activity.Children.Add(new TextBlock
        {
            Text = "Butchi",
            FontSize = 12,
            FontWeight = FontWeight.Bold
        });
        activity.Children.Add(new TextBlock
        {
            Text = status,
            FontSize = 11,
            Opacity = 0.72
        });
        activity.SetValue(Grid.ColumnProperty, 1);
        island.Children.Add(activity);

        var local = new Border
        {
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(999),
            Background = ButchiTheme.LocalStatusSurfaceBrush(ActualThemeVariant),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "Local",
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = ButchiTheme.LocalStatusForegroundBrush(ActualThemeVariant)
            }
        };
        local.SetValue(Grid.ColumnProperty, 2);
        island.Children.Add(local);

        return new Border
        {
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(999),
            Background = ButchiTheme.CardSurfaceBrush(ActualThemeVariant),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            Child = island
        };
    }

    private Control BuildExpandedIsland()
    {
        var selected = ViewModel.SelectedState;
        var bothActionsEnabled = ViewModel.TranslateEnabled && ViewModel.RewriteEnabled;
        var root = new StackPanel { Spacing = 12 };

        root.Children.Add(BuildSourceOverlay());

        if (ViewModel.TranslateEnabled && ViewModel.SelectedAction == TextAction.Translate)
            root.Children.Add(BuildLanguageSelector());

        if (!string.IsNullOrWhiteSpace(selected.Reasoning))
            root.Children.Add(BuildThinkingDisclosure(selected));

        root.Children.Add(BuildResultPanel(selected, bothActionsEnabled));

        return new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(24),
            Background = ButchiTheme.NavigationSurfaceBrush(ActualThemeVariant),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            Child = root
        };
    }

    private Control BuildSourceOverlay()
    {
        var overlay = new Grid();

        if (!string.IsNullOrWhiteSpace(ViewModel.SourceText))
        {
            var source = BuildSourcePreview();
            source.Margin = new Thickness(0, 18, 0, 0);
            overlay.Children.Add(source);
        }

        var chrome = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            Margin = new Thickness(14, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Top
        };

        var modes = BuildOverlayModeControls();
        modes.SetValue(Grid.ColumnProperty, 1);
        chrome.Children.Add(modes);

        var actions = BuildHeaderActions();
        actions.SetValue(Grid.ColumnProperty, 2);
        chrome.Children.Add(actions);

        overlay.Children.Add(chrome);
        return overlay;
    }

    private Control BuildOverlayModeControls()
    {
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        controls.Children.Add(new Image
        {
            Source = BrandAssets.CreateBitmap(),
            Width = 30,
            Height = 30,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 5, 0)
        });

        if (ViewModel.TranslateEnabled)
            controls.Children.Add(ModeIconButton("文A", "Translate", TextAction.Translate));

        if (ViewModel.RewriteEnabled)
            controls.Children.Add(ModeIconButton("✎", "Rewrite", TextAction.Rewrite));

        return new Border
        {
            Padding = new Thickness(5),
            CornerRadius = new CornerRadius(28),
            Background = ButchiTheme.CardSurfaceBrush(ActualThemeVariant),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = controls
        };
    }

    private Button ModeIconButton(string glyph, string tooltip, TextAction action)
    {
        var selected = ViewModel.SelectedAction == action;
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = action == TextAction.Translate ? 18 : 20,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = 46,
            Height = 46,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = selected ? ButchiTheme.CobaltBrush : Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        if (selected) button.Foreground = ButchiTheme.WhiteBrush;
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => ViewModel.SelectAction(action);
        return button;
    }

    private Control BuildHeaderActions()
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 7, 0, 0)
        };

        var pinTooltip = _controller.IsPinned ? "Unpin popover" : "Pin popover";
        var pinGlyph = _controller.IsPinned ? "\uE77A" : "\uE718";
        var pin = HeaderIconButton(
            pinGlyph,
            pinTooltip,
            _controller.IsPinned,
            new FontFamily("Segoe MDL2 Assets"));
        pin.Click += (_, _) =>
        {
            _controller.TogglePinned();
            RefreshContent();
        };
        actions.Children.Add(pin);

        actions.Children.Add(new Border
        {
            Width = 1,
            Height = 24,
            Margin = new Thickness(2, 0),
            Background = ButchiTheme.DividerBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        var close = HeaderIconButton("×", "Close popover");
        close.Click += (_, _) => HidePersistent();
        actions.Children.Add(close);

        return actions;
    }

    private Button HeaderIconButton(
        string glyph,
        string tooltip,
        bool selected = false,
        FontFamily? fontFamily = null)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = fontFamily is null ? glyph == "×" ? 22 : 17 : 16,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (fontFamily is not null)
            icon.FontFamily = fontFamily;

        var button = new Button
        {
            Content = icon,
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(17),
            Background = selected ? ButchiTheme.CobaltBrush : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (selected) button.Foreground = ButchiTheme.WhiteBrush;
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private Control BuildSourcePreview()
    {
        var content = new StackPanel { Spacing = 7 };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        header.Children.Add(new TextBlock
        {
            Text = "SOURCE",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Opacity = 0.55,
            LetterSpacing = 1,
            VerticalAlignment = VerticalAlignment.Center
        });

        var chevron = new TextBlock
        {
            Text = ViewModel.IsSourceExpanded ? "⌃" : "›",
            FontSize = 19,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center
        };
        chevron.SetValue(Grid.ColumnProperty, 1);
        header.Children.Add(chevron);
        content.Children.Add(header);

        content.Children.Add(new TextBlock
        {
            Text = ViewModel.SourcePreviewText,
            FontSize = 12,
            TextWrapping = ViewModel.IsSourceExpanded ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = ViewModel.IsSourceExpanded ? TextTrimming.None : TextTrimming.CharacterEllipsis
        });

        var toggle = new Button
        {
            Content = content,
            Padding = new Thickness(14, 30, 14, 11),
            CornerRadius = new CornerRadius(16),
            Background = ButchiTheme.CardSurfaceBrush(ActualThemeVariant),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ToolTip.SetTip(toggle, ViewModel.IsSourceExpanded ? "Collapse source" : "Expand source");
        toggle.Click += (_, _) => ViewModel.RequestToggleSource();
        return toggle;
    }

    private Control BuildLanguageSelector()
    {
        var language = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7
        };
        language.Children.Add(new TextBlock
        {
            Text = "To",
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(2, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        foreach (var item in new[] { "Vietnamese", "English", "Japanese" })
        {
            var button = new Button
            {
                Content = item,
                Padding = new Thickness(10, 5),
                CornerRadius = new CornerRadius(9),
                FontSize = 11
            };
            if (string.Equals(ViewModel.TargetLanguage, item, StringComparison.OrdinalIgnoreCase))
            {
                button.Background = ButchiTheme.CobaltBrush;
                button.Foreground = ButchiTheme.WhiteBrush;
            }
            button.Click += (_, _) => ViewModel.RequestFavoriteLanguage(item);
            language.Children.Add(button);
        }

        return language;
    }

    private Control BuildThinkingDisclosure(ActionPresentationState selected)
    {
        var section = new StackPanel { Spacing = 7 };
        var thinkingLabel = selected.IsRunning && string.IsNullOrEmpty(selected.Output)
            ? "Thinking…"
            : "Thinking";

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };
        header.Children.Add(new TextBlock
        {
            Text = "✦",
            FontSize = 14,
            Foreground = ButchiTheme.CobaltBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        var label = new TextBlock
        {
            Text = thinkingLabel,
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetValue(Grid.ColumnProperty, 1);
        header.Children.Add(label);

        var chevron = new TextBlock
        {
            Text = selected.IsThinkingExpanded ? "⌃" : "⌄",
            FontSize = 13,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        };
        chevron.SetValue(Grid.ColumnProperty, 2);
        header.Children.Add(chevron);

        var toggle = new Button
        {
            Content = header,
            Padding = new Thickness(12, 8),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        toggle.Click += (_, _) => ViewModel.RequestToggleThinking();

        section.Children.Add(toggle);

        if (selected.IsThinkingExpanded)
        {
            section.Children.Add(new ScrollViewer
            {
                MaxHeight = 120,
                Margin = new Thickness(12, 0, 12, 6),
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = selected.Reasoning,
                    FontSize = 11,
                    Opacity = 0.6,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = ButchiTheme.CardSurfaceBrush(ActualThemeVariant),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            Child = section
        };
    }

    private Control BuildResultPanel(ActionPresentationState selected, bool bothActionsEnabled)
    {
        var result = new StackPanel { Spacing = 10 };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        header.Children.Add(new TextBlock
        {
            Text = selected.IsRunning ? "WORKING" : selected.ErrorMessage is null ? "RESULT" : "ERROR",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = selected.ErrorMessage is null
                ? ButchiTheme.CobaltBrush
                : new SolidColorBrush(ButchiTheme.Error),
            LetterSpacing = 1,
            VerticalAlignment = VerticalAlignment.Center
        });

        var actions = BuildResultActions(selected);
        actions.SetValue(Grid.ColumnProperty, 1);
        header.Children.Add(actions);
        result.Children.Add(header);

        Control body;
        if (selected.IsRunning)
        {
            body = new TextBlock
            {
                Text = string.IsNullOrEmpty(selected.Output) ? "Running locally…" : selected.Output + " ▍",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };
        }
        else if (selected.ErrorMessage is { } error)
        {
            body = new TextBlock
            {
                Text = error,
                FontSize = 13,
                Foreground = new SolidColorBrush(ButchiTheme.Error),
                TextWrapping = TextWrapping.Wrap
            };
        }
        else if (!string.IsNullOrWhiteSpace(selected.Output))
        {
            body = new TextBlock
            {
                Text = selected.Output,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
        }
        else
        {
            body = new TextBlock
            {
                Text = bothActionsEnabled
                    ? "Select Translate or Rewrite to run on the selected text."
                    : $"Starting {ViewModel.SelectedAction.ToString().ToLowerInvariant()} locally…",
                FontSize = 12,
                Opacity = 0.62,
                TextWrapping = TextWrapping.Wrap
            };
        }

        result.Children.Add(new ScrollViewer
        {
            MaxHeight = ResultScrollMaxHeight,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = body
        });

        return new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = ButchiTheme.CardSurfaceBrush(ActualThemeVariant),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            Child = result
        };
    }

    private Control BuildResultActions(ActionPresentationState selected)
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (selected.IsRunning || (string.IsNullOrWhiteSpace(selected.Output) && selected.ErrorMessage is null))
            return actions;

        var rerun = CompactActionIconButton("↻", "Run again");
        rerun.Click += (_, _) => ViewModel.RequestRerun();
        actions.Children.Add(rerun);

        if (!string.IsNullOrWhiteSpace(selected.Output))
        {
            var copy = CompactActionIconButton("⧉", "Copy");
            copy.Click += (_, _) => ViewModel.RequestCopy();
            actions.Children.Add(copy);

            var replace = CompactActionIconButton("⇄", "Replace");
            replace.Click += (_, _) => ViewModel.RequestReplace();
            actions.Children.Add(replace);
        }

        return actions;
    }

    private static Button CompactActionIconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(17),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private void OnActionStarted(object? sender, TextAction action)
    {
        if (action != ViewModel.SelectedAction) return;
        _controller.HandleWorkStarted();
    }

    private async void OnActionFinished(object? sender, TextAction action)
    {
        if (action != ViewModel.SelectedAction) return;
        if (await _controller.HandleResultCompletedAsync(ViewModel.AutoHideDelay))
            Dispatcher.UIThread.Post(Hide);
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _controller.HandlePointerEntered();
    }

    private async void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (await _controller.HandlePointerExitedAsync(ViewModel.AutoHideDelay))
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
        ViewModel.ActionStarted -= OnActionStarted;
        ViewModel.ActionFinished -= OnActionFinished;
        _controller.Dispose();
        Closing -= OnClosing;
        Close();
    }
}
