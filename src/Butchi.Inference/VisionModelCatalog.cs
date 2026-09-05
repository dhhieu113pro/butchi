namespace Butchi.Inference;

public sealed record VisionModelOption(
    string Repo,
    string ModelFile,
    string ProjectorFile);

public static class VisionModelCatalog
{
    public static VisionModelOption Default { get; } = new(
        "LiquidAI/LFM2.5-VL-450M-GGUF",
        "LFM2.5-VL-450M-Q4_K_M.gguf",
        "mmproj-LFM2.5-VL-450m-Q8_0.gguf");
}
