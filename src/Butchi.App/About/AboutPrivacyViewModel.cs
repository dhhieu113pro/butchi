using Butchi.App.History;
using Butchi.Infrastructure;

namespace Butchi.App.About;

public sealed record AboutPrivacyMetadata(string Version, string ProjectName, string License, string ProjectUrl);
public sealed record AboutRuntimeStatus(bool IsModelLoaded, string? Backend, string? Device);

public interface ILocalAiDataCleanup
{
    ValueTask DeleteLocalDataAsync(CancellationToken cancellationToken);
}

public sealed class LocalAiDataCleanup(IHistoryStore history, LocalAiDataManager modelData) : ILocalAiDataCleanup
{
    public async ValueTask DeleteLocalDataAsync(CancellationToken cancellationToken)
    {
        await history.ClearAsync(cancellationToken);
        await modelData.ClearModelsAsync(cancellationToken);
    }
}

public sealed class AboutPrivacyViewModel(
    ILocalAiDataCleanup cleanup,
    AboutPrivacyMetadata metadata,
    AboutRuntimeStatus runtimeStatus)
{
    public string Version => metadata.Version;
    public string ProjectName => metadata.ProjectName;
    public string License => metadata.License;
    public string ProjectUrl => metadata.ProjectUrl;
    public bool IsModelLoaded => runtimeStatus.IsModelLoaded;
    public string? Backend => runtimeStatus.Backend;
    public string? Device => runtimeStatus.Device;
    public string DeleteStatus { get; private set; } = string.Empty;

    public async ValueTask DeleteLocalDataAsync(bool confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed) return;

        DeleteStatus = "Deleting local data…";
        try
        {
            await cleanup.DeleteLocalDataAsync(cancellationToken);
            DeleteStatus = "Local data deleted";
        }
        catch
        {
            DeleteStatus = "Delete failed";
            throw;
        }
    }
}
