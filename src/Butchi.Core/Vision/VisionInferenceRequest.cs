using Butchi.Core.Configuration;

namespace Butchi.Core.Vision;

public sealed record VisionInferenceRequest(
    string Prompt,
    byte[] ImagePng,
    uint MaxTokens,
    float Temperature,
    uint Seed);

public interface IVisionInferenceEngine : IAsyncDisposable
{
    IAsyncEnumerable<string> GenerateAsync(
        VisionInferenceRequest request,
        AppConfig config,
        CancellationToken cancellationToken);
}
