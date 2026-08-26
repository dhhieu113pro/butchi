using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butchi.App.Styling;

namespace Butchi.App.Settings;

public sealed class PromptsView : ScrollViewer
{
    private readonly PromptsViewModel _viewModel;
    private readonly TextBlock _saveStatus;
    private readonly Button _translateButton;
    private readonly Button _rewriteButton;
    private readonly ComboBox _profiles;
    private readonly TextBox _promptEditor;
    private bool _ready;

    public PromptsView(PromptsViewModel viewModel)
    {
        _viewModel = viewModel;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

        _saveStatus = new TextBlock
        {
            Text = viewModel.SaveStatus,
            FontSize = 12,
            Opacity = 0.72,
            VerticalAlignment = VerticalAlignment.Center
        };

        _translateButton = ModeButton("Translate", PromptMode.Translate);
        _rewriteButton = ModeButton("Rewrite", PromptMode.Rewrite);
        _profiles = new ComboBox
        {
            MinWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            DisplayMemberBinding = new Avalonia.Data.Binding(nameof(PromptProfile.Name))
        };
        _promptEditor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 280,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(14)
        };

        _profiles.SelectionChanged += async (_, _) =>
        {
            if (!_ready || _profiles.SelectedItem is not PromptProfile profile)
                return;
            await RunAsync(() => _viewModel.SetProfileAsync(profile.Name, CancellationToken.None));
            RefreshFromViewModel();
        };

        _promptEditor.LostFocus += async (_, _) =>
        {
            if (!_ready)
                return;
            await RunAsync(() => _viewModel.SetPromptTextAsync(_promptEditor.Text ?? string.Empty, CancellationToken.None));
            RefreshFromViewModel();
        };

        Content = BuildContent();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PromptsViewModel.SaveStatus))
                _saveStatus.Text = _viewModel.SaveStatus;
        };

        RefreshFromViewModel();
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
        content.Children.Add(BuildEditorCard());
        return content;
    }

    private Control BuildHeader()
    {
        var titleStack = new StackPanel { Spacing = 5 };
        titleStack.Children.Add(new TextBlock
        {
            Text = "PROMPTS",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = ButchiTheme.CobaltBrush
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Shape Translate and Rewrite",
            FontSize = 30,
            FontWeight = FontWeight.SemiBold
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Choose a starting profile, then tune the local system prompt to match how you work.",
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

    private Control BuildEditorCard()
    {
        var mode = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        mode.Children.Add(_translateButton);
        mode.Children.Add(_rewriteButton);

        var editor = new StackPanel { Spacing = 16 };
        editor.Children.Add(SectionHeading("Prompt profile", "Switch modes without losing either prompt. Editing a preset becomes Custom automatically."));
        editor.Children.Add(Field("Mode", mode));
        editor.Children.Add(Field("Profile", _profiles));
        editor.Children.Add(Field("System prompt", _promptEditor));
        editor.Children.Add(new TextBlock
        {
            Text = "The prompt is stored locally and sent only to the local inference runtime when that action runs.",
            FontSize = 12,
            Opacity = 0.64,
            TextWrapping = TextWrapping.Wrap
        });

        return new Border
        {
            Child = editor,
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = ButchiTheme.DividerBrush,
            Background = ButchiTheme.SubtleSurfaceBrush
        };
    }

    private Button ModeButton(string text, PromptMode mode)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(18, 9),
            CornerRadius = new CornerRadius(9),
            MinWidth = 120
        };
        button.Click += (_, _) =>
        {
            _viewModel.SetMode(mode);
            RefreshFromViewModel();
        };
        return button;
    }

    private void RefreshFromViewModel()
    {
        _translateButton.Background = _viewModel.Mode == PromptMode.Translate ? ButchiTheme.CobaltBrush : Brushes.Transparent;
        _translateButton.Foreground = _viewModel.Mode == PromptMode.Translate ? ButchiTheme.WhiteBrush : null;
        _rewriteButton.Background = _viewModel.Mode == PromptMode.Rewrite ? ButchiTheme.CobaltBrush : Brushes.Transparent;
        _rewriteButton.Foreground = _viewModel.Mode == PromptMode.Rewrite ? ButchiTheme.WhiteBrush : null;

        _profiles.ItemsSource = _viewModel.Profiles;
        _profiles.SelectedItem = _viewModel.Profiles.First(profile => profile.Name == _viewModel.SelectedProfile.Name);
        _promptEditor.Text = _viewModel.PromptText;
        _saveStatus.Text = _viewModel.SaveStatus;
    }

    private static Control SectionHeading(string title, string subtitle)
    {
        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock { Text = subtitle, FontSize = 13, Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
        return stack;
    }

    private static Control Field(string label, Control control)
    {
        var stack = new StackPanel { Spacing = 7 };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(control);
        return stack;
    }

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
        }
    }
}
