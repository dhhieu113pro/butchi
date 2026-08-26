using Butchi.Core.Configuration;
using Butchi.Infrastructure;

namespace Butchi.App.Settings;

public sealed class JsonAppConfigStoreAdapter(JsonConfigStore inner) : IAppConfigStore
{
    public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
        new(inner.LoadAsync(cancellationToken));

    public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken) =>
        new(inner.SaveAsync(config, cancellationToken));
}
