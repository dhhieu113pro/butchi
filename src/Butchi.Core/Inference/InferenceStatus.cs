namespace Butchi.Core.Inference;

public sealed record InferenceStatus(
    bool IsLoaded,
    string? ModelRepo = null,
    string? ModelFile = null,
    string? ActualBackend = null,
    string? ActualDevice = null);
