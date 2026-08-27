using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butchi.App.Styling;

namespace Butchi.App.About;

public sealed class AboutPrivacyView : UserControl
{
    private readonly AboutPrivacyViewModel _viewModel;
    private readonly TextBlock _deleteStatus = new() { FontSize = 11, Opacity = 0.72 };

    public AboutPrivacyView(AboutPrivacyViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        var content = new StackPanel
        {
            Margin = new Thickness(36, 30, 42, 48),
            Spacing = 18,
            MaxWidth = 860,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        content.Children.Add(new TextBlock { Text = "ABOUT & PRIVACY", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ButchiTheme.CobaltBrush, LetterSpacing = 1.2 });
        content.Children.Add(new TextBlock { Text = "Local by design", FontSize = 30, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = "Butchi runs translation and rewrite inference on this Windows device. Text is not sent to a hosted AI service. Network access is used only when you choose to download a model.", FontSize = 14, Opacity = 0.74, TextWrapping = TextWrapping.Wrap });

        content.Children.Add(Card(BuildStatus()));
        content.Children.Add(Card(BuildProject()));
        content.Children.Add(BuildDangerZone());
        content.Children.Add(_deleteStatus);
        Content = new ScrollViewer { Content = content };
        Refresh();
    }

    private Control BuildStatus()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Runtime status", FontSize = 18, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = _viewModel.IsModelLoaded ? "Model ready" : "Model not loaded",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = _viewModel.IsModelLoaded ? new SolidColorBrush(ButchiTheme.Success) : new SolidColorBrush(ButchiTheme.Warning)
        });
        panel.Children.Add(new TextBlock { Text = $"Backend: {_viewModel.Backend ?? "Auto / not active"}", FontSize = 13, Opacity = 0.72 });
        panel.Children.Add(new TextBlock { Text = $"Device: {_viewModel.Device ?? "Local device"}", FontSize = 13, Opacity = 0.72 });
        return panel;
    }

    private Control BuildProject()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Project", FontSize = 18, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(Row("Application", _viewModel.ProjectName));
        panel.Children.Add(Row("Version", _viewModel.Version));
        panel.Children.Add(Row("License", _viewModel.License));
        panel.Children.Add(Row("Source", _viewModel.ProjectUrl));
        return panel;
    }

    private Control BuildDangerZone()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Delete local AI data", FontSize = 18, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Clears private history and downloaded models. Your preferences, prompts, theme, language, and other settings are kept.", FontSize = 13, Opacity = 0.72, TextWrapping = TextWrapping.Wrap });
        var delete = new Button { Content = "Delete history & models", Padding = new Thickness(15, 8), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left };
        delete.Click += async (_, _) => { await _viewModel.DeleteLocalDataAsync(true, CancellationToken.None); Refresh(); };
        panel.Children.Add(delete);
        return new Border { Padding = new Thickness(20), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(ButchiTheme.Warning), Child = panel };
    }

    private static Control Row(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
        grid.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.65 });
        var text = new TextBlock { Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        text.SetValue(Grid.ColumnProperty, 1);
        grid.Children.Add(text);
        return grid;
    }

    private void Refresh() => _deleteStatus.Text = _viewModel.DeleteStatus;

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
