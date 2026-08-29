using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butchi.App.About;
using Butchi.App.Branding;
using Butchi.App.History;
using Butchi.App.Models;
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
    private readonly ModelManagementView _modelView;
    private readonly HistoryView _historyView;
    private readonly AboutPrivacyView _aboutPrivacyView;
    private readonly ManagementContentScroll _contentScroll;
    private readonly Border _contentHost;
    private readonly Border _navigationHost = new();
    private readonly Dictionary<ManagementPage, NavigationEntry> _navigationButtons = [];

    public ManagementWindow(
        ManagementShellViewModel viewModel,
        GeneralSettingsViewModel generalSettings,
        PromptsViewModel prompts,
        ModelManagementViewModel models,
        HistoryViewModel history,
        AboutPrivacyViewModel aboutPrivacy,
        Action<AppThemePreference> applyTheme,
        bool autoPrepareModel = true)
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
        _modelView = new ModelManagementView(models, autoPrepareModel);
        _historyView = new HistoryView(history);
        _aboutPrivacyView = new AboutPrivacyView(aboutPrivacy);
        _contentScroll = new ManagementContentScroll(_generalView);
        _contentHost = new Border { Padding = new Thickness(0), Child = _contentScroll };

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("248,*") };
        root.Children.Add(_navigationHost);
        _contentHost.SetValue(Grid.ColumnProperty, 1);
        root.Children.Add(_contentHost);
        Content = root;

        RebuildNavigation();
        ActualThemeVariantChanged += (_, _) => RebuildNavigation();
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    public void Show(ManagementPage page)
    {
        Select(page);
        if (IsVisible) Activate(); else base.Show();
    }

    private void RebuildNavigation()
    {
        _navigationButtons.Clear();
        _navigationHost.Background = ButchiTheme.NavigationSurfaceBrush(ActualThemeVariant);
        _navigationHost.Child = BuildNavigationContent();
        RefreshNavigation();
    }

    private Control BuildNavigationContent()
    {
        var foreground = ButchiTheme.NavigationForegroundBrush(ActualThemeVariant);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(18, 22, 18, 26) };
        brand.Children.Add(new Image { Source = BrandAssets.CreateBitmap(), Width = 34, Height = 34, Stretch = Stretch.Uniform });
        var brandText = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock { Text = "Butchi", FontSize = 20, FontWeight = FontWeight.Bold, Foreground = foreground });
        brandText.Children.Add(new TextBlock { Text = "Local AI for Windows", FontSize = 10, Opacity = 0.72, Foreground = foreground });
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
            Foreground = foreground,
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
        return grid;
    }

    private Button NavButton(string title, string subtitle, ManagementPage page)
    {
        var indicator = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(999),
            Margin = new Thickness(0, 2, 10, 2),
            Background = Brushes.Transparent
        };
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        text.Children.Add(new TextBlock { Text = subtitle, FontSize = 10, Opacity = 0.68 });

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        content.Children.Add(indicator);
        text.SetValue(Grid.ColumnProperty, 1);
        content.Children.Add(text);

        var button = new Button
        {
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(11, 10),
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            Foreground = ButchiTheme.NavigationForegroundBrush(ActualThemeVariant)
        };
        button.Click += (_, _) => Select(page);
        _navigationButtons[page] = new NavigationEntry(button, indicator);
        return button;
    }

    private void Select(ManagementPage page)
    {
        _viewModel.Select(page);
        _contentScroll.Show(BuildPage(page));
        RefreshNavigation();
    }

    private Control BuildPage(ManagementPage page) => page switch
    {
        ManagementPage.General => _generalView,
        ManagementPage.Prompts => _promptsView,
        ManagementPage.Model => _modelView,
        ManagementPage.History => _historyView,
        ManagementPage.AboutPrivacy => _aboutPrivacyView,
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null)
    };

    private void RefreshNavigation()
    {
        foreach (var (page, entry) in _navigationButtons)
        {
            var selected = page == _viewModel.SelectedPage;
            entry.Button.Background = selected
                ? ButchiTheme.SelectedNavigationSurfaceBrush(ActualThemeVariant)
                : Brushes.Transparent;
            entry.Button.Foreground = selected
                ? ButchiTheme.SelectedNavigationForegroundBrush(ActualThemeVariant)
                : ButchiTheme.NavigationForegroundBrush(ActualThemeVariant);
            entry.Indicator.Background = selected
                ? ButchiTheme.SelectedNavigationIndicatorBrush(ActualThemeVariant)
                : Brushes.Transparent;
        }
    }

    private sealed record NavigationEntry(Button Button, Border Indicator);
}

public sealed class ManagementContentScroll : ScrollViewer
{
    public ManagementContentScroll(Control content)
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        Content = content;
    }

    public void Show(Control content)
    {
        Content = content;
        Offset = Vector.Zero;
    }
}
