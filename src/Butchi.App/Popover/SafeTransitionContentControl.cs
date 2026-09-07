global using TransitioningContentControl = Butchi.App.Popover.SafeTransitionContentControl;

using Avalonia.Animation;
using Avalonia.Controls;

namespace Butchi.App.Popover;

/// <summary>
/// Startup-safe replacement for Avalonia's TransitioningContentControl.
///
/// Butchi creates the popover before it is shown. With Avalonia 12.1.1 the framework
/// TransitioningContentControl can receive a logical-tree attach notification after its
/// logical parent has been cleared and throws:
/// "AttachedToLogicalTreeCore called for 'TransitioningContentControl' but control has no logical parent."
///
/// A normal ContentControl does not keep outgoing content attached for page transitions,
/// so it avoids that lifecycle edge case. Keep the PageTransition-shaped property so the
/// existing popover presentation code remains source-compatible; transitions are deliberately
/// ignored until Avalonia's logical-tree lifecycle is safe for this startup path.
/// </summary>
internal sealed class SafeTransitionContentControl : ContentControl
{
    public IPageTransition? PageTransition { get; set; }
}
