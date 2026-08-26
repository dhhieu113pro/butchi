namespace Butchi.Core.Inference;

public sealed record InferenceRequest(
    string Prompt,
    uint MaxTokens,
    float Temperature,
    uint Seed);
