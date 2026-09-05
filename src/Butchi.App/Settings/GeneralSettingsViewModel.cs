using System.ComponentModel;
using System.Runtime.CompilerServices;
using Butchi.Core.Configuration;
using Butchi.Core.Platform;

namespace Butchi.App.Settings;

public sealed class GeneralSettingsViewModel : INotifyPropertyChanged
{
    private readonly IAppConfigStore _store;
    private readonly IAutoStartService _autoStart;
    private AppConfig _config;
    private string _saveStatus = "Saved";

    private GeneralSettingsViewModel(
        IAppConfigStore store,
        IAutoStartService autoStart,
        AppConfig config)
    {
        _store = store;
        _autoStart = autoStart;
        _config = config;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppThemePreference Theme => _config.Theme;
    public bool LaunchAtLogin => _config.LaunchAtLogin;
    public bool TranslateEnabled => _config.TranslateEnabled;
    public bool RewriteEnabled => _config.RewriteEnabled;
    public string TargetLanguage => _config.TargetLanguage;
    public IReadOnlyList<string> FavoriteLanguages => _config.FavoriteLanguages;
    public ResultAction ResultAction => _config.ResultAction;
    public uint PopoverHideSeconds => _config.PopoverHideSeconds;
    public string SaveStatus => _saveStatus;

    public static async ValueTask<GeneralSettingsViewModel> CreateAsync(
        IAppConfigStore store,
        IAutoStartService autoStart,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(autoStart);

        var config = await store.LoadAsync(cancellationToken);
        var actualLaunchAtLogin = await autoStart.GetEnabledAsync(cancellationToken);
        if (config.LaunchAtLogin != actualLaunchAtLogin)
        {
            config = config with { LaunchAtLogin = actualLaunchAtLogin };
            await store.SaveAsync(config, cancellationToken);
        }

        return new GeneralSettingsViewModel(store, autoStart, config);
    }

    public ValueTask SetThemeAsync(AppThemePreference value, CancellationToken cancellationToken) =>
        UpdateAsync(_config with { Theme = value }, cancellationToken);

    public async ValueTask SetLaunchAtLoginAsync(bool value, CancellationToken cancellationToken)
    {
        if (value == _config.LaunchAtLogin)
            return;

        var previous = _config.LaunchAtLogin;
        SetSaveStatus("Saving…");
        try
        {
            if (value)
                await _autoStart.EnableAsync(cancellationToken);
            else
                await _autoStart.DisableAsync(cancellationToken);

            var actual = await _autoStart.GetEnabledAsync(cancellationToken);
            if (actual != value)
                throw new InvalidOperationException("Launch-at-login state did not match the requested value.");

            var candidate = _config with { LaunchAtLogin = value };
            await _store.SaveAsync(candidate, cancellationToken);
            _config = candidate;
            OnPropertyChanged(nameof(LaunchAtLogin));
            SetSaveStatus("Saved");
        }
        catch
        {
            await RestoreAutoStartBestEffortAsync(previous);
            OnPropertyChanged(nameof(LaunchAtLogin));
            SetSaveStatus("Couldn't save");
            throw;
        }
    }

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

    private async ValueTask RestoreAutoStartBestEffortAsync(bool enabled)
    {
        try
        {
            if (enabled)
                await _autoStart.EnableAsync(CancellationToken.None);
            else
                await _autoStart.DisableAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the original failure. The displayed value remains the last persisted state.
        }
    }

    private void RaiseAllGeneralProperties()
    {
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(LaunchAtLogin));
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
