using System.Text.Json;
using System.Text.Json.Serialization;
using Butchi.Core.Configuration;

namespace Butchi.Infrastructure;

public enum ConfigLoadState
{
    Ready,
    Missing,
    Invalid,
    Unavailable
}

public sealed record ConfigLoadResult(
    AppConfig Config,
    ConfigLoadState State,
    string? ErrorCode = null);

public sealed class JsonConfigStore
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _options;
    private readonly Func<string, Stream> _openRead;

    public JsonConfigStore(AppPaths paths) : this(paths, File.OpenRead)
    {
    }

    internal JsonConfigStore(AppPaths paths, Func<string, Stream> openRead)
    {
        _paths = paths;
        _openRead = openRead;
        _options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default) =>
        (await LoadWithStatusAsync(cancellationToken).ConfigureAwait(false)).Config;

    public async Task<ConfigLoadResult> LoadWithStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.ConfigPath))
        {
            return new ConfigLoadResult(AppConfig.Default, ConfigLoadState.Missing);
        }

        try
        {
            await using var stream = _openRead(_paths.ConfigPath);
            var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, _options, cancellationToken)
                .ConfigureAwait(false);
            if (config is null)
                return new ConfigLoadResult(AppConfig.Default, ConfigLoadState.Invalid, nameof(JsonException));
            return !IsUsable(config)
                ? new ConfigLoadResult(AppConfig.Default, ConfigLoadState.Invalid, "InvalidConfiguration")
                : new ConfigLoadResult(config, ConfigLoadState.Ready);
        }
        catch (JsonException ex)
        {
            return new ConfigLoadResult(AppConfig.Default, ConfigLoadState.Invalid, ex.GetType().Name);
        }
        catch (IOException ex)
        {
            return new ConfigLoadResult(AppConfig.Default, ConfigLoadState.Unavailable, ex.GetType().Name);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ConfigLoadResult(AppConfig.Default, ConfigLoadState.Unavailable, ex.GetType().Name);
        }
    }

    private static bool IsUsable(AppConfig config) =>
        !string.IsNullOrWhiteSpace(config.TargetLanguage)
        && config.FavoriteLanguages is not null
        && config.FavoriteLanguages.Count <= 5
        && config.FavoriteLanguages.All(language => !string.IsNullOrWhiteSpace(language))
        && config.RewriteSystemPrompt is not null
        && config.TranslateSystemPrompt is not null
        && !string.IsNullOrWhiteSpace(config.ModelRepo)
        && !string.IsNullOrWhiteSpace(config.ModelFile);

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        await using var stream = File.Create(_paths.ConfigPath);
        await JsonSerializer.SerializeAsync(stream, config, _options, cancellationToken);
    }
}
