namespace Butchi.Core.Platform;

public interface IAutoStartService
{
    ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken);
    ValueTask EnableAsync(CancellationToken cancellationToken);
    ValueTask DisableAsync(CancellationToken cancellationToken);
}
