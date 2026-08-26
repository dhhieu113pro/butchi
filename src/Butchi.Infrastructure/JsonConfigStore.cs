using System.Text.Json;
using System.Text.Json.Serialization;
using Butchi.Core.Configuration;

namespace Butchi.Infrastructure;

public sealed class JsonConfigStore
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _options;

    public JsonConfigStore(AppPaths paths)
    {
        _paths = paths;
        _options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.ConfigPath))
        {
            return AppConfig.Default;
        }

        try
        {
            await using var stream = File.OpenRead(_paths.ConfigPath);
            return await JsonSerializer.DeserializeAsync<AppConfig>(stream, _options, cancellationToken)
                   ?? AppConfig.Default;
        }
        catch (JsonException)
        {
            return AppConfig.Default;
        }
        catch (IOException)
        {
            return AppConfig.Default;
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        await using var stream = File.Create(_paths.ConfigPath);
        await JsonSerializer.SerializeAsync(stream, config, _options, cancellationToken);
    }
}
