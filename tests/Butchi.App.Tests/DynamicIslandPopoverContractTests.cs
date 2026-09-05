using Xunit;

namespace Butchi.App.Tests;

public sealed class DynamicIslandPopoverContractTests
{
    [Fact]
    public void Popover_window_switches_between_compact_island_and_expanded_content()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("CompactWidth = 420", source, StringComparison.Ordinal);
        Assert.Contains("ExpandedWidth = 760", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsCompact", source, StringComparison.Ordinal);
        Assert.Contains("BuildCompactIsland", source, StringComparison.Ordinal);
        Assert.Contains("BuildExpandedIsland", source, StringComparison.Ordinal);
        Assert.Contains("Translating…", source, StringComparison.Ordinal);
        Assert.Contains("Rewriting…", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Expanded_popover_matches_the_approved_compact_result_first_design()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("BuildSourceOverlay", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildPrimaryHeader", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildCenteredLogo", source, StringComparison.Ordinal);
        Assert.Contains("ModeIconButton", source, StringComparison.Ordinal);
        Assert.Contains("TextTrimming.CharacterEllipsis", source, StringComparison.Ordinal);
        Assert.Contains("BuildThinkingDisclosure", source, StringComparison.Ordinal);
        Assert.Contains("BuildResultPanel", source, StringComparison.Ordinal);
        Assert.Contains("BuildResultActions", source, StringComparison.Ordinal);
        Assert.Contains("CompactActionIconButton", source, StringComparison.Ordinal);
        Assert.Contains("ResultScrollMaxHeight = 340", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = ResultScrollMaxHeight", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight = 180", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFooterActions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FooterButton", source, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Local AI\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"On device\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Expanded_popover_overlays_logo_and_icon_only_modes_on_the_source_card()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("BuildSourceOverlay", source, StringComparison.Ordinal);
        Assert.Contains("BuildOverlayModeControls", source, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(0, 18, 0, 0)", source, StringComparison.Ordinal);
        Assert.Contains("Width = 46", source, StringComparison.Ordinal);
        Assert.Contains("Height = 46", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = tooltip", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth = action == TextAction.Translate ? 112 : 104", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.SetTip(button, tooltip)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Expanded_popover_header_keeps_pin_and_close_actions_visible()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("BuildHeaderActions", source, StringComparison.Ordinal);
        Assert.Contains("HeaderIconButton", source, StringComparison.Ordinal);
        Assert.Contains("Pin popover", source, StringComparison.Ordinal);
        Assert.Contains("Unpin popover", source, StringComparison.Ordinal);
        Assert.Contains("Close popover", source, StringComparison.Ordinal);
        Assert.Contains("Segoe MDL2 Assets", source, StringComparison.Ordinal);
        Assert.Contains("\\uE718", source, StringComparison.Ordinal);
        Assert.Contains("\\uE77A", source, StringComparison.Ordinal);
        Assert.Contains("HeaderIconButton(\"×\", \"Close popover\")", source, StringComparison.Ordinal);
        Assert.Contains("_controller.TogglePinned()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Popover_window_uses_a_transparent_host_so_the_rounded_surface_defines_its_visible_shape()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("TransparencyLevelHint = [WindowTransparencyLevel.Transparent]", source, StringComparison.Ordinal);
        Assert.Contains("Background = Brushes.Transparent", source, StringComparison.Ordinal);
        Assert.Contains("CornerRadius = new CornerRadius(24)", source, StringComparison.Ordinal);
        Assert.Contains("Background = ButchiTheme.NavigationSurfaceBrush(ActualThemeVariant)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Popover_animates_only_between_compact_and_expanded_states()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("TransitioningContentControl", source, StringComparison.Ordinal);
        Assert.Contains("CrossFade", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(180)", source, StringComparison.Ordinal);
        Assert.Contains("compact != _lastCompactState", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Popover_wires_result_aware_inactivity_lifecycle()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var viewModelPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverViewModel.cs");
        var window = File.ReadAllText(windowPath);
        var viewModel = File.ReadAllText(viewModelPath);

        Assert.Contains("ActionStarted", viewModel, StringComparison.Ordinal);
        Assert.Contains("ActionFinished", viewModel, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ActionStarted += OnActionStarted", window, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ActionFinished += OnActionFinished", window, StringComparison.Ordinal);
        Assert.Contains("HandleWorkStarted", window, StringComparison.Ordinal);
        Assert.Contains("HandleResultCompletedAsync", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_popover_deactivation_respects_the_pin_guard()
    {
        var root = FindRepositoryRoot();
        var factoryPath = Path.Combine(root, "src", "Butchi.App", "Startup", "ButchiRuntimeFactory.cs");
        var source = File.ReadAllText(factoryPath);

        Assert.Contains("popover.Deactivated +=", source, StringComparison.Ordinal);
        Assert.Contains("if (popoverWindowController.HandleDeactivated())", source, StringComparison.Ordinal);
        Assert.Contains("popover.Hide()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_row_is_collapsed_by_default_and_clickable_to_expand()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("Text = ViewModel.SourcePreviewText", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsSourceExpanded ? TextWrapping.Wrap : TextWrapping.NoWrap", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsSourceExpanded ? TextTrimming.None : TextTrimming.CharacterEllipsis", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.RequestToggleSource()", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Butchi.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Butchi repository root.");
    }
}
