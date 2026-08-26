using System.ComponentModel;
using System.Runtime.CompilerServices;
using Butchi.Core.Configuration;

namespace Butchi.App.Settings;

public enum PromptMode
{
    Translate,
    Rewrite
}

public sealed record PromptProfile(string Name, string Prompt);

public sealed class PromptsViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<PromptProfile> TranslateProfiles =
    [
        new("Balanced", AppConfig.Default.TranslateSystemPrompt),
        new("Literal", "You are a precise translation assistant. Translate the user's text as literally as possible into the target language while preserving terminology, names, numbers, formatting, and meaning. Output only the translation with no quotes or explanation."),
        new("Natural", "You are a natural translation assistant. Translate the user's text into fluent, idiomatic target-language prose while preserving meaning, tone, names, numbers, and formatting. Output only the translation with no quotes or explanation."),
        new("Custom", string.Empty)
    ];

    private static readonly IReadOnlyList<PromptProfile> RewriteProfiles =
    [
        new("Balanced", AppConfig.Default.RewriteSystemPrompt),
        new("Concise", "You are a precise writing assistant. Rewrite the user's text to be concise, clear, natural, and grammatically correct. Preserve the original meaning and language. Remove repetition and unnecessary words. Output only the rewritten text with no quotes or explanation."),
        new("Polished", "You are a precise writing assistant. Rewrite the user's text with polished, professional, natural wording and correct grammar while preserving the original meaning and language. Output only the rewritten text with no quotes or explanation."),
        new("Custom", string.Empty)
    ];

    private readonly IAppConfigStore _store;
    private AppConfig _config;
    private PromptMode _mode = PromptMode.Translate;
    private string _saveStatus = "Saved";

    private PromptsViewModel(IAppConfigStore store, AppConfig config)
    {
        _store = store;
        _config = config;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PromptMode Mode => _mode;
    public IReadOnlyList<PromptProfile> Profiles => _mode == PromptMode.Translate ? TranslateProfiles : RewriteProfiles;
    public string PromptText => _mode == PromptMode.Translate ? _config.TranslateSystemPrompt : _config.RewriteSystemPrompt;
    public PromptProfile SelectedProfile => ResolveProfile(PromptText, Profiles);
    public string SaveStatus => _saveStatus;

    public static async ValueTask<PromptsViewModel> CreateAsync(
        IAppConfigStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        var config = await store.LoadAsync(cancellationToken);
        return new PromptsViewModel(store, config);
    }

    public void SetMode(PromptMode mode)
    {
        if (_mode == mode)
            return;

        _mode = mode;
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(PromptText));
        OnPropertyChanged(nameof(SelectedProfile));
    }

    public async ValueTask SetProfileAsync(string profileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        var profile = Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, profileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
            throw new ArgumentException($"Unknown prompt profile '{profileName}'.", nameof(profileName));

        if (profile.Name == "Custom")
        {
            OnPropertyChanged(nameof(SelectedProfile));
            return;
        }

        await SetPromptTextAsync(profile.Prompt, cancellationToken);
    }

    public ValueTask SetPromptTextAsync(string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        var candidate = _mode == PromptMode.Translate
            ? _config with { TranslateSystemPrompt = value }
            : _config with { RewriteSystemPrompt = value };
        return UpdateAsync(candidate, cancellationToken);
    }

    private async ValueTask UpdateAsync(AppConfig candidate, CancellationToken cancellationToken)
    {
        if (candidate == _config)
            return;

        SetSaveStatus("Saving…");
        try
        {
            await _store.SaveAsync(candidate, cancellationToken);
            _config = candidate;
            OnPropertyChanged(nameof(PromptText));
            OnPropertyChanged(nameof(SelectedProfile));
            SetSaveStatus("Saved");
        }
        catch
        {
            SetSaveStatus("Couldn't save");
            throw;
        }
    }

    private static PromptProfile ResolveProfile(string prompt, IReadOnlyList<PromptProfile> profiles) =>
        profiles.FirstOrDefault(profile =>
            profile.Name != "Custom" && string.Equals(profile.Prompt, prompt, StringComparison.Ordinal))
        ?? profiles.Single(profile => profile.Name == "Custom");

    private void SetSaveStatus(string value)
    {
        if (_saveStatus == value)
            return;
        _saveStatus = value;
        OnPropertyChanged(nameof(SaveStatus));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
