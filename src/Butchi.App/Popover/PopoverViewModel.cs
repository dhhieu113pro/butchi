using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Butchi.Core.Actions;

namespace Butchi.App.Popover;

public sealed class PopoverViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<TextAction, StringBuilder> _pending = new()
    {
        [TextAction.Translate] = new StringBuilder(),
        [TextAction.Rewrite] = new StringBuilder()
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? TranslateLanguageRequested;

    public ActionPresentationState Translate { get; private set; } = ActionPresentationState.Empty;
    public ActionPresentationState Rewrite { get; private set; } = ActionPresentationState.Empty;
    public TimeSpan AutoHideDelay { get; set; } = TimeSpan.FromSeconds(6);
    public bool IsAutoHideArmed { get; private set; }

    public void Begin(TextAction action, long runId)
    {
        _pending[action].Clear();
        SetState(action, new ActionPresentationState(runId, string.Empty, true));
    }

    public bool Append(TextAction action, long runId, string chunk)
    {
        var state = GetState(action);
        if (state.RunId != runId)
            return false;

        _pending[action].Append(chunk);
        return true;
    }

    public void FlushPendingUpdates()
    {
        Flush(TextAction.Translate);
        Flush(TextAction.Rewrite);
    }

    public void RequestFavoriteLanguage(string language) =>
        TranslateLanguageRequested?.Invoke(this, language);

    public void ArmAutoHide()
    {
        IsAutoHideArmed = true;
        OnPropertyChanged(nameof(IsAutoHideArmed));
    }

    public void CancelAutoHide()
    {
        IsAutoHideArmed = false;
        OnPropertyChanged(nameof(IsAutoHideArmed));
    }

    private void Flush(TextAction action)
    {
        var pending = _pending[action];
        if (pending.Length == 0)
            return;

        var state = GetState(action);
        SetState(action, state with { Output = state.Output + pending.ToString() });
        pending.Clear();
    }

    private ActionPresentationState GetState(TextAction action) =>
        action == TextAction.Translate ? Translate : Rewrite;

    private void SetState(TextAction action, ActionPresentationState state)
    {
        if (action == TextAction.Translate)
        {
            Translate = state;
            OnPropertyChanged(nameof(Translate));
        }
        else
        {
            Rewrite = state;
            OnPropertyChanged(nameof(Rewrite));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
