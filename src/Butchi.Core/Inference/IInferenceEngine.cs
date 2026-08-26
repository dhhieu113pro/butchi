using Butchi.Core.Configuration;

namespace Butchi.Core.Inference;

public interface IInferenceEngine : IAsyncDisposable
{
    Task LoadAsync(AppConfig config, CancellationToken cancellationToken);
    Task UnloadAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<string> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken);
    InferenceStatus GetStatus();
}
