using Butchi.Core.Inference;

namespace Butchi.App.Composition;

public sealed class ButchiAppServices : IServiceProvider, IDisposable
{
    private readonly IInferenceEngine _inferenceEngine;

    private ButchiAppServices(IInferenceEngine inferenceEngine)
    {
        _inferenceEngine = inferenceEngine;
    }

    public static ButchiAppServices CreateForTesting(IInferenceEngine inferenceEngine) =>
        new(inferenceEngine ?? throw new ArgumentNullException(nameof(inferenceEngine)));

    public object? GetService(Type serviceType) =>
        serviceType == typeof(IInferenceEngine) ? _inferenceEngine : null;

    public void Dispose()
    {
    }
}
