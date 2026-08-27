namespace Butchi.App.Popover;

public sealed record ActionPresentationState(long RunId, string Output, bool IsRunning, string? ErrorMessage = null)
{
    public static ActionPresentationState Empty { get; } = new(0, string.Empty, false);
}
