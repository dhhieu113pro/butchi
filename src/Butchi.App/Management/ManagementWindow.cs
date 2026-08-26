using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butchi.App.Tray;

namespace Butchi.App.Management;

public sealed class ManagementWindow : Window, IManagementWindowHost
{
    private readonly ManagementShellViewModel _viewModel;
    private readonly TextBlock _pageTitle;

    public ManagementWindow(ManagementShellViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        Title = "Butchi";
        Width = 900;
        Height = 620;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _pageTitle = new TextBlock
        {
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(24),
            Text = PageTitle(viewModel.SelectedPage)
        };

        Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("200,*"),
            Children =
            {
                BuildNavigation(),
                new Border
                {
                    [Grid.ColumnProperty] = 1,
                    Child = _pageTitle
                }
            }
        };

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
        var panel = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8
        };

        panel.Children.Add(NavButton("Settings", ManagementPage.Settings));
        panel.Children.Add(NavButton("History", ManagementPage.History));
        panel.Children.Add(NavButton("Models", ManagementPage.Models));
        panel.Children.Add(NavButton("Status", ManagementPage.Status));

        return new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(8),
            Child = panel
        };
    }

    private Button NavButton(string text, ManagementPage page)
    {
        var button = new Button
        {
            Content = text,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        button.Click += (_, _) => Select(page);
        return button;
    }

    private void Select(ManagementPage page)
    {
        _viewModel.Select(page);
        _pageTitle.Text = PageTitle(page);
    }

    private static string PageTitle(ManagementPage page) => page switch
    {
        ManagementPage.Settings => "Settings",
        ManagementPage.History => "History",
        ManagementPage.Models => "Models",
        ManagementPage.Status => "Status",
        _ => page.ToString()
    };
}
