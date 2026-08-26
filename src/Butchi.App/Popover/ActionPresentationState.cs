namespace Butchi.App.Popover;

public sealed record ActionPresentationState(long RunId, string Output, bool IsRunning)
{
    public static ActionPresentationState Empty { get; } = new(0, string.Empty, false);
}
