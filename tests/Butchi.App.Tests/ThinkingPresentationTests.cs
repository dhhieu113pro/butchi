using Butchi.App.Popover;
using Butchi.Core.Actions;
using Xunit;

namespace Butchi.App.Tests;

public sealed class ThinkingPresentationTests
{
    [Fact]
    public void Reasoning_is_expanded_while_streaming_then_collapses_when_answer_starts()
    {
        var reasoningProperty = typeof(ActionPresentationState).GetProperty("Reasoning");
        var expandedProperty = typeof(ActionPresentationState).GetProperty("IsThinkingExpanded");
        var appendReasoning = typeof(PopoverViewModel).GetMethod("AppendReasoning");
        var toggleThinking = typeof(PopoverViewModel).GetMethod("RequestToggleThinking");

        Assert.NotNull(reasoningProperty);
        Assert.NotNull(expandedProperty);
        Assert.NotNull(appendReasoning);
        Assert.NotNull(toggleThinking);

        var vm = new PopoverViewModel();
        vm.Begin(TextAction.Translate, 1);

        Assert.Equal(true, appendReasoning.Invoke(vm, [TextAction.Translate, 1L, "checking context"]));
        vm.FlushPendingUpdates();
        Assert.Equal("checking context", reasoningProperty.GetValue(vm.Translate));
        Assert.Equal(true, expandedProperty.GetValue(vm.Translate));

        Assert.True(vm.Append(TextAction.Translate, 1, "final answer"));
        vm.FlushPendingUpdates();
        Assert.Equal("final answer", vm.Translate.Output);
        Assert.Equal(false, expandedProperty.GetValue(vm.Translate));

        toggleThinking.Invoke(vm, null);
        Assert.Equal(true, expandedProperty.GetValue(vm.Translate));
    }

    [Fact]
    public void Popover_renders_muted_collapsible_thinking_separately_from_result()
    {
        var root = FindRepositoryRoot();
        var popoverPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var popover = File.ReadAllText(popoverPath);

        Assert.Contains("Thinking", popover, StringComparison.Ordinal);
        Assert.Contains("RequestToggleThinking", popover, StringComparison.Ordinal);
        Assert.Contains("FontSize = 11", popover, StringComparison.Ordinal);
        Assert.Contains("Opacity = 0.6", popover, StringComparison.Ordinal);
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
