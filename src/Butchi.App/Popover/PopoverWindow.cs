using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Butchi.App.Branding;

namespace Butchi.App.Popover;

public sealed class PopoverWindow : Window
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
        Width = 420;
        MinHeight = 360;
        MaxHeight = 760;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Content = BuildContent(profile.UseBoundedScroll);
        KeyDown += OnKeyDown;
        Closing += OnClosing;
    }

    public PopoverViewModel ViewModel { get; }
    public Guid InstanceId => _controller.InstanceId;

    public void ShowPersistent()
    {
        _controller.Show();
        if (!IsVisible)
            Show();
        else
            Activate();
    }

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

    private Control BuildContent(bool boundedScroll)
    {
        var translate = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.TextProperty] = new Binding("Translate.Output")
        };

        var rewrite = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.TextProperty] = new Binding("Rewrite.Output")
        };

        var panel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Translate", FontWeight = FontWeight.SemiBold },
                translate,
                new TextBlock { Text = "Rewrite", FontWeight = FontWeight.SemiBold },
                rewrite
            }
        };

        Control content = boundedScroll
            ? new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
            : panel;

        return new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            Child = content
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        _controller.HandleEscape();
        Hide();
        e.Handled = true;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_controller.IsDisposed)
            return;

        e.Cancel = true;
        HidePersistent();
    }

    public void Destroy()
    {
        if (_controller.IsDisposed)
            return;

        _controller.Dispose();
        Closing -= OnClosing;
        Close();
    }
}
