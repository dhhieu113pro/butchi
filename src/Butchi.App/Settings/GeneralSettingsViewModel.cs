using System.ComponentModel;
using System.Runtime.CompilerServices;
using Butchi.Core.Configuration;

namespace Butchi.App.Settings;

public sealed class GeneralSettingsViewModel : INotifyPropertyChanged
{
    private readonly IAppConfigStore _store;
    private AppConfig _config;
    private string _saveStatus = "Saved";

    private GeneralSettingsViewModel(IAppConfigStore store, AppConfig config)
    {
        _store = store;
        _config = config;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppThemePreference Theme => _config.Theme;
    public bool TranslateEnabled => _config.TranslateEnabled;
    public bool RewriteEnabled => _config.RewriteEnabled;
    public string TargetLanguage => _config.TargetLanguage;
    public IReadOnlyList<string> FavoriteLanguages => _config.FavoriteLanguages;
    public ResultAction ResultAction => _config.ResultAction;
    public uint PopoverHideSeconds => _config.PopoverHideSeconds;
    public string SaveStatus => _saveStatus;

    public static async ValueTask<GeneralSettingsViewModel> CreateAsync(
        IAppConfigStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        var config = await store.LoadAsync(cancellationToken);
        return new GeneralSettingsViewModel(store, config);
    }

    public ValueTask SetThemeAsync(AppThemePreference value, CancellationToken cancellationToken) =>
        UpdateAsync(_config with { Theme = value }, cancellationToken);

    public ValueTask SetTranslateEnabledAsync(bool value, CancellationToken cancellationToken) =>
        UpdateAsync(_config with { TranslateEnabled = value }, cancellationToken);

    public ValueTask SetRewriteEnabledAsync(bool value, CancellationToken cancellationToken) =>
        UpdateAsync(_config with { RewriteEnabled = value }, cancellationToken);

    public ValueTask SetTargetLanguageAsync(string value, CancellationToken cancellationToken)
    {
        var normalized = AppConfig.NormalizeTargetLanguage(value);
        return UpdateAsync(_config with { TargetLanguage = normalized }, cancellationToken);
    }

    public ValueTask SetFavoriteLanguagesAsync(
        IReadOnlyList<string> values,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > 5)
            throw new ArgumentException("Favorite languages cannot contain more than 5 values.", nameof(values));

        var normalized = values
            .Select(AppConfig.NormalizeTargetLanguage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return UpdateAsync(_config with { FavoriteLanguages = normalized }, cancellationToken);
    }

    public ValueTask SetResultActionAsync(ResultAction value, CancellationToken cancellationToken) =>
        UpdateAsync(_config with { ResultAction = value }, cancellationToken);

    public ValueTask SetPopoverHideSecondsAsync(uint value, CancellationToken cancellationToken) =>
        UpdateAsync(_config with { PopoverHideSeconds = Math.Clamp(value, 2u, 30u) }, cancellationToken);

    private async ValueTask UpdateAsync(AppConfig candidate, CancellationToken cancellationToken)
    {
        if (candidate == _config)
            return;

        SetSaveStatus("Saving…");
        try
        {
            await _store.SaveAsync(candidate, cancellationToken);
            _config = candidate;
            RaiseAllGeneralProperties();
            SetSaveStatus("Saved");
        }
        catch
        {
            SetSaveStatus("Couldn't save");
            throw;
        }
    }

    private void RaiseAllGeneralProperties()
    {
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(TranslateEnabled));
        OnPropertyChanged(nameof(RewriteEnabled));
        OnPropertyChanged(nameof(TargetLanguage));
        OnPropertyChanged(nameof(FavoriteLanguages));
        OnPropertyChanged(nameof(ResultAction));
        OnPropertyChanged(nameof(PopoverHideSeconds));
    }

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
