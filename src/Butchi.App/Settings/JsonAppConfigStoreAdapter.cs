using Butchi.Core.Configuration;
using Butchi.Infrastructure;

namespace Butchi.App.Settings;

public interface IStartupConfigStore : IAppConfigStore
{
    ValueTask<ConfigLoadResult> LoadWithStatusAsync(CancellationToken cancellationToken);
}

public sealed class JsonAppConfigStoreAdapter(JsonConfigStore inner) : IStartupConfigStore
{
    public ValueTask<ConfigLoadResult> LoadWithStatusAsync(CancellationToken cancellationToken) =>
        new(inner.LoadWithStatusAsync(cancellationToken));

    public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
        new(inner.LoadAsync(cancellationToken));

    public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken) =>
        new(inner.SaveAsync(config, cancellationToken));
}
