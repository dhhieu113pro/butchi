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
            return config is null
                ? new ConfigLoadResult(AppConfig.Default, ConfigLoadState.Invalid, nameof(JsonException))
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

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        await using var stream = File.Create(_paths.ConfigPath);
        await JsonSerializer.SerializeAsync(stream, config, _options, cancellationToken);
    }
}
