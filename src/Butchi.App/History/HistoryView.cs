using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butchi.App.Styling;
using Butchi.Core.History;

namespace Butchi.App.History;

public sealed class HistoryView : UserControl
{
    private readonly HistoryViewModel _viewModel;
    private readonly StackPanel _results = new() { Spacing = 12 };
    private readonly TextBlock _state = new() { FontSize = 13, Opacity = 0.72 };
    private readonly TextBlock _saveStatus = new() { FontSize = 11, Opacity = 0.68 };

    public HistoryView(HistoryViewModel viewModel)
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

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(new TextBlock { Text = "HISTORY", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ButchiTheme.CobaltBrush, LetterSpacing = 1.2 });
        heading.Children.Add(new TextBlock { Text = "Private local results", FontSize = 30, FontWeight = FontWeight.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Search, copy, or remove Translate and Rewrite results stored only on this device.", FontSize = 14, Opacity = 0.72, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(heading);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        var refresh = SecondaryButton("Refresh");
        refresh.Click += async (_, _) => { await _viewModel.RefreshAsync(CancellationToken.None); Refresh(); };
        var clear = SecondaryButton("Clear history");
        clear.Click += async (_, _) => { await _viewModel.ClearAsync(true, CancellationToken.None); Refresh(); };
        actions.Children.Add(refresh);
        actions.Children.Add(clear);
        actions.SetValue(Grid.ColumnProperty, 1);
        header.Children.Add(actions);
        content.Children.Add(header);

        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,190"), ColumnSpacing = 10 };
        var search = new TextBox { PlaceholderText = "Search source or result…", MinHeight = 38, Text = _viewModel.Query };
        search.KeyUp += async (_, _) => { _viewModel.Query = search.Text; await _viewModel.RefreshAsync(CancellationToken.None); Refresh(); };
        filters.Children.Add(search);
        var filter = new ComboBox { ItemsSource = new[] { "All actions", "Translate", "Rewrite" }, SelectedIndex = 0, MinHeight = 38 };
        filter.SelectionChanged += async (_, _) =>
        {
            _viewModel.ActionFilter = filter.SelectedIndex switch { 1 => "translate", 2 => "rewrite", _ => null };
            await _viewModel.RefreshAsync(CancellationToken.None);
            Refresh();
        };
        filter.SetValue(Grid.ColumnProperty, 1);
        filters.Children.Add(filter);
        content.Children.Add(filters);

        content.Children.Add(_state);
        content.Children.Add(_results);
        content.Children.Add(BuildRetention());
        content.Children.Add(_saveStatus);

        Content = new ScrollViewer { Content = content };
        Refresh();
    }

    private Control BuildRetention()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,180") };
        var copy = new StackPanel { Spacing = 3 };
        copy.Children.Add(new TextBlock { Text = "Retention", FontSize = 14, FontWeight = FontWeight.SemiBold });
        copy.Children.Add(new TextBlock { Text = "Automatically remove older local history. Use 0 to clear all retained entries.", FontSize = 12, Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(copy);
        var days = new NumericUpDown { Minimum = 0, Maximum = 3650, Value = _viewModel.RetentionDays, Increment = 1, MinHeight = 36 };
        days.SetValue(Grid.ColumnProperty, 1);
        days.ValueChanged += async (_, _) =>
        {
            if (days.Value is { } value)
            {
                await _viewModel.SetRetentionDaysAsync((int)value, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CancellationToken.None);
                Refresh();
            }
        };
        grid.Children.Add(days);
        return new Border { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(18), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = ButchiTheme.DividerBrush, Background = ButchiTheme.SubtleSurfaceBrush, Child = grid };
    }

    private void Refresh()
    {
        _results.Children.Clear();
        _state.Text = _viewModel.IsLoading ? "Loading history…" : _viewModel.ErrorMessage is { } error ? $"Could not load history: {error}" : _viewModel.IsEmpty ? "No history yet. Translate or Rewrite text and results will appear here." : $"{_viewModel.Items.Count} local result{(_viewModel.Items.Count == 1 ? string.Empty : "s")}";
        _saveStatus.Text = $"Retention · {_viewModel.SaveStatus}";

        foreach (var entry in _viewModel.Items)
            _results.Children.Add(EntryCard(entry));
    }

    private Control EntryCard(HistoryEntry entry)
    {
        var panel = new StackPanel { Spacing = 10 };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var label = entry.Action.Equals("translate", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.TargetLanguage)
            ? $"Translate → {entry.TargetLanguage}"
            : char.ToUpperInvariant(entry.Action[0]) + entry.Action[1..];
        top.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = ButchiTheme.CobaltBrush });
        var when = DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampMs).ToLocalTime().ToString("g");
        var time = new TextBlock { Text = when, FontSize = 11, Opacity = 0.6 };
        time.SetValue(Grid.ColumnProperty, 1);
        top.Children.Add(time);
        panel.Children.Add(top);
        panel.Children.Add(new TextBlock { Text = entry.Source, FontSize = 12, Opacity = 0.68, TextWrapping = TextWrapping.Wrap, MaxHeight = 62 });
        panel.Children.Add(new TextBlock { Text = entry.Result, FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 90 });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var copy = SecondaryButton("Copy result");
        copy.Click += async (_, _) => await _viewModel.CopyResultAsync(entry, CancellationToken.None);
        var delete = SecondaryButton("Delete");
        delete.Click += async (_, _) => { await _viewModel.DeleteAsync(entry, CancellationToken.None); Refresh(); };
        actions.Children.Add(copy);
        actions.Children.Add(delete);
        panel.Children.Add(actions);

        return new Border { Padding = new Thickness(18), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = ButchiTheme.DividerBrush, Background = ButchiTheme.SubtleSurfaceBrush, Child = panel };
    }

    private static Button SecondaryButton(string text) => new() { Content = text, Padding = new Thickness(13, 7), CornerRadius = new CornerRadius(8) };
}
