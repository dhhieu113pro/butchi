using Butchi.Core.Configuration;

namespace Butchi.Core.Inference;

public interface IInferenceEngine : IAsyncDisposable
{
    Task LoadAsync(AppConfig config, CancellationToken cancellationToken);
    Task UnloadAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<string> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken);

    async IAsyncEnumerable<InferenceStreamChunk> GenerateDetailedAsync(
        InferenceRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chunk in GenerateAsync(request, cancellationToken).WithCancellation(cancellationToken))
        {
            yield return new InferenceStreamChunk(InferenceStreamChunkKind.Answer, chunk);
        }
    }

    InferenceStatus GetStatus();
}
