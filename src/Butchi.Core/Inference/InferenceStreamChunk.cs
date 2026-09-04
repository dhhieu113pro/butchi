namespace Butchi.Core.Inference;

public enum InferenceStreamChunkKind
{
    Reasoning,
    Answer
}

public sealed record InferenceStreamChunk(InferenceStreamChunkKind Kind, string Text);
