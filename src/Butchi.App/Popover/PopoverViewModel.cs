using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;

namespace Butchi.App.Popover;

public sealed class PopoverViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<TextAction, StringBuilder> _pending = new()
    {
        [TextAction.Translate] = new StringBuilder(),
        [TextAction.Rewrite] = new StringBuilder()
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<TextAction>? ActionRequested;
    public event EventHandler<string>? TranslateLanguageRequested;
    public event EventHandler<TextAction>? RerunRequested;
    public event EventHandler<string>? CopyRequested;
    public event EventHandler<string>? ReplaceRequested;

    public ActionPresentationState Translate { get; private set; } = ActionPresentationState.Empty;
    public ActionPresentationState Rewrite { get; private set; } = ActionPresentationState.Empty;
    public string SourceText { get; private set; } = string.Empty;
    public TextAction SelectedAction { get; private set; } = TextAction.Translate;
    public string? TargetLanguage { get; private set; }
    public bool TranslateEnabled { get; private set; } = true;
    public bool RewriteEnabled { get; private set; } = true;
    public TimeSpan AutoHideDelay { get; set; } = TimeSpan.FromSeconds(6);
    public bool IsAutoHideArmed { get; private set; }

    public ActionPresentationState SelectedState => GetState(SelectedAction);
    public bool IsCompact =>
        SelectedState.IsRunning &&
        string.IsNullOrWhiteSpace(SelectedState.Output) &&
        SelectedState.ErrorMessage is null;

    public void SetSession(string sourceText, TextAction action, string? targetLanguage)
    {
        SetSessionCore(sourceText, action, targetLanguage, translateEnabled: true, rewriteEnabled: true);
    }

    public TextAction? SetSession(string sourceText, AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var enabledActions = AutomationRules.GetEnabledActions(config);
        var initialAction = enabledActions.Count > 0 ? enabledActions[0] : TextAction.Translate;
        SetSessionCore(
            sourceText,
            initialAction,
            config.TargetLanguage,
            config.TranslateEnabled,
            config.RewriteEnabled);

        return enabledActions.Count > 0 ? initialAction : null;
    }

    public void SelectAction(TextAction action)
    {
        if (!IsActionEnabled(action))
            return;

        SelectedAction = action;
        OnPropertyChanged(nameof(SelectedAction));
        OnPropertyChanged(nameof(SelectedState));
        OnPropertyChanged(nameof(IsCompact));
        ActionRequested?.Invoke(this, action);
    }

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

    public bool Complete(TextAction action, long runId)
    {
        Flush(action);
        var state = GetState(action);
        if (state.RunId != runId)
            return false;
        SetState(action, state with { IsRunning = false, ErrorMessage = null });
        return true;
    }

    public bool Fail(TextAction action, long runId, string message)
    {
        var state = GetState(action);
        if (state.RunId != runId)
            return false;
        _pending[action].Clear();
        SetState(action, state with { IsRunning = false, ErrorMessage = message });
        return true;
    }

    public void FlushPendingUpdates()
    {
        Flush(TextAction.Translate);
        Flush(TextAction.Rewrite);
    }

    public void RequestFavoriteLanguage(string language)
    {
        if (!TranslateEnabled)
            return;

        TargetLanguage = language;
        OnPropertyChanged(nameof(TargetLanguage));
        TranslateLanguageRequested?.Invoke(this, language);
    }

    public void RequestRerun()
    {
        if (IsActionEnabled(SelectedAction))
            RerunRequested?.Invoke(this, SelectedAction);
    }

    public void RequestCopy()
    {
        var output = SelectedState.Output;
        if (!string.IsNullOrWhiteSpace(output)) CopyRequested?.Invoke(this, output);
    }

    public void RequestReplace()
    {
        var output = SelectedState.Output;
        if (!string.IsNullOrWhiteSpace(output)) ReplaceRequested?.Invoke(this, output);
    }

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

    private void SetSessionCore(
        string sourceText,
        TextAction action,
        string? targetLanguage,
        bool translateEnabled,
        bool rewriteEnabled)
    {
        _pending[TextAction.Translate].Clear();
        _pending[TextAction.Rewrite].Clear();
        Translate = ActionPresentationState.Empty;
        Rewrite = ActionPresentationState.Empty;
        SourceText = sourceText ?? string.Empty;
        SelectedAction = action;
        TargetLanguage = string.IsNullOrWhiteSpace(targetLanguage) ? null : targetLanguage.Trim();
        TranslateEnabled = translateEnabled;
        RewriteEnabled = rewriteEnabled;
        OnPropertyChanged(nameof(Translate));
        OnPropertyChanged(nameof(Rewrite));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(SelectedAction));
        OnPropertyChanged(nameof(TargetLanguage));
        OnPropertyChanged(nameof(TranslateEnabled));
        OnPropertyChanged(nameof(RewriteEnabled));
        OnPropertyChanged(nameof(SelectedState));
        OnPropertyChanged(nameof(IsCompact));
    }

    private bool IsActionEnabled(TextAction action) =>
        action == TextAction.Translate ? TranslateEnabled : RewriteEnabled;

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

        if (SelectedAction == action)
        {
            OnPropertyChanged(nameof(SelectedState));
            OnPropertyChanged(nameof(IsCompact));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
