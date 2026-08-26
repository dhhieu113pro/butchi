using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butchi.App.Branding;
using Butchi.App.Settings;
using Butchi.App.Styling;
using Butchi.App.Tray;
using Butchi.Core.Configuration;

namespace Butchi.App.Management;

public sealed class ManagementWindow : Window, IManagementWindowHost
{
    private readonly ManagementShellViewModel _viewModel;
    private readonly GeneralSettingsView _generalView;
    private readonly PromptsView _promptsView;
    private readonly Border _contentHost;
    private readonly Dictionary<ManagementPage, Button> _navigationButtons = [];

    public ManagementWindow(
        ManagementShellViewModel viewModel,
        GeneralSettingsViewModel generalSettings,
        PromptsViewModel prompts,
        Action<AppThemePreference> applyTheme)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        Title = "Butchi Settings";
        Icon = BrandAssets.CreateWindowIcon();
        Width = 1120;
        Height = 760;
        MinWidth = 860;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _generalView = new GeneralSettingsView(generalSettings, applyTheme);
        _promptsView = new PromptsView(prompts);
        _contentHost = new Border
        {
            Padding = new Thickness(0),
            Child = _generalView
        };

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("248,*") };
        root.Children.Add(BuildNavigation());
        _contentHost.SetValue(Grid.ColumnProperty, 1);
        root.Children.Add(_contentHost);
        Content = root;

        RefreshNavigation();

        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    public void Show(ManagementPage page)
    {
        Select(page);
        if (IsVisible)
            Activate();
        else
            base.Show();
    }

    private Control BuildNavigation()
    {
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(18, 22, 18, 26)
        };
        brand.Children.Add(new Image
        {
            Source = BrandAssets.CreateBitmap(),
            Width = 34,
            Height = 34,
            Stretch = Stretch.Uniform
        });
        var brandText = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock
        {
            Text = "Butchi",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = ButchiTheme.WhiteBrush
        });
        brandText.Children.Add(new TextBlock
        {
            Text = "Local AI for Windows",
            FontSize = 10,
            Opacity = 0.72,
            Foreground = ButchiTheme.WhiteBrush
        });
        brand.Children.Add(brandText);

        var nav = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0, 12, 0) };
        nav.Children.Add(NavButton("General", "Everyday preferences", ManagementPage.General));
        nav.Children.Add(NavButton("Prompts", "Translate & Rewrite", ManagementPage.Prompts));
        nav.Children.Add(NavButton("Model", "Local GGUF runtime", ManagementPage.Model));
        nav.Children.Add(NavButton("History", "Private local results", ManagementPage.History));
        nav.Children.Add(NavButton("About & Privacy", "Status and local data", ManagementPage.AboutPrivacy));

        var footer = new TextBlock
        {
            Text = "Private by default\nYour text stays on this device.",
            FontSize = 11,
            Opacity = 0.7,
            Foreground = ButchiTheme.WhiteBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(18, 18, 18, 22),
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        grid.Children.Add(brand);
        nav.SetValue(Grid.RowProperty, 1);
        grid.Children.Add(nav);
        footer.SetValue(Grid.RowProperty, 2);
        grid.Children.Add(footer);

        return new Border
        {
            Background = ButchiTheme.CobaltDarkBrush,
            Child = grid
        };
    }

    private Button NavButton(string title, string subtitle, ManagementPage page)
    {
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        text.Children.Add(new TextBlock { Text = subtitle, FontSize = 10, Opacity = 0.68 });

        var button = new Button
        {
            Content = text,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            Foreground = ButchiTheme.WhiteBrush
        };
        button.Click += (_, _) => Select(page);
        _navigationButtons[page] = button;
        return button;
    }

    private void Select(ManagementPage page)
    {
        _viewModel.Select(page);
        _contentHost.Child = BuildPage(page);
        RefreshNavigation();
    }

    private Control BuildPage(ManagementPage page) => page switch
    {
        ManagementPage.General => _generalView,
        ManagementPage.Prompts => _promptsView,
        ManagementPage.Model => Placeholder(
            "MODEL",
            "Download, load, and tune the local GGUF model.",
            "The existing model runtime stays intact while Task 14.3 restores its full management surface."),
        ManagementPage.History => Placeholder(
            "HISTORY",
            "Search private Translate and Rewrite results stored on this device.",
            "Task 14.4 will connect the existing SQLite history service to this surface."),
        ManagementPage.AboutPrivacy => Placeholder(
            "ABOUT & PRIVACY",
            "Local processing, runtime status, version, and data controls.",
            "Task 14.5 will fill this surface with live status and privacy controls."),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null)
    };

    private static Control Placeholder(string eyebrow, string title, string detail)
    {
        var content = new StackPanel
        {
            Margin = new Thickness(36, 30, 42, 48),
            Spacing = 16,
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        content.Children.Add(new TextBlock
        {
            Text = eyebrow,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = ButchiTheme.CobaltBrush,
            LetterSpacing = 1.2
        });
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 30,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            Background = ButchiTheme.SubtleSurfaceBrush,
            Child = new TextBlock
            {
                Text = detail,
                FontSize = 14,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap
            }
        });
        return new ScrollViewer { Content = content };
    }

    private void RefreshNavigation()
    {
        foreach (var (page, button) in _navigationButtons)
        {
            var selected = page == _viewModel.SelectedPage;
            button.Background = selected ? ButchiTheme.WhiteBrush : Brushes.Transparent;
            button.Foreground = selected ? ButchiTheme.CobaltDarkBrush : ButchiTheme.WhiteBrush;
        }
    }
}
