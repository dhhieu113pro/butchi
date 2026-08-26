namespace Butchi.Inference;

public sealed record ModelOption(
    string Id,
    string Label,
    string Repo,
    string File,
    string SizeHint);

public static class ModelCatalog
{
    public static IReadOnlyList<ModelOption> Options { get; } =
    [
        new("qwen35-0.8b-q4", "Qwen3.5 0.8B (Q4_K_M) — default", "unsloth/Qwen3.5-0.8B-GGUF", "Qwen3.5-0.8B-Q4_K_M.gguf", "~530 MB"),
        new("qwen35-0.8b-q5", "Qwen3.5 0.8B (Q5_K_M)", "unsloth/Qwen3.5-0.8B-GGUF", "Qwen3.5-0.8B-Q5_K_M.gguf", "~590 MB"),
        new("qwen3-0.6b-q4", "Qwen3 0.6B (Q4_K_M)", "unsloth/Qwen3-0.6B-GGUF", "Qwen3-0.6B-Q4_K_M.gguf", "~400 MB")
    ];
}
