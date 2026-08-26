using Butchi.Core.Configuration;
using Butchi.Core.Inference;

namespace Butchi.Inference;

public sealed record ModelLoadRequest(
    string ModelRepo,
    string ModelFile,
    BackendPreference BackendPreference,
    uint GpuLayers,
    uint ContextSize)
{
    public static ModelLoadRequest FromConfig(AppConfig config)
    {
        var contextSize = Math.Max(10_000u, checked(config.MaxTokens + 2_048u));
        return new ModelLoadRequest(
            config.ModelRepo,
            config.ModelFile,
            config.BackendPreference,
            config.GpuLayers,
            contextSize);
    }
}

public interface ILLamaRuntimeFactory
{
    Task<ILLamaRuntime> LoadAsync(ModelLoadRequest request, CancellationToken cancellationToken);
}

public interface ILLamaRuntime : IAsyncDisposable
{
    IAsyncEnumerable<string> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken);
}
