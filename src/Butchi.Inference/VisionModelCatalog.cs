using Butchi.Core.Configuration;

namespace Butchi.Inference;

public sealed record VisionModelOption(
    string Repo,
    string ModelFile,
    string ProjectorFile,
    string Label)
{
    public override string ToString() => Label;
}

public static class VisionModelCatalog
{
    public static IReadOnlyList<VisionModelOption> Options { get; } =
    [
        new(
            "LiquidAI/LFM2.5-VL-450M-GGUF",
            "LFM2.5-VL-450M-Q4_K_M.gguf",
            "mmproj-LFM2.5-VL-450m-Q8_0.gguf",
            "LFM2.5-VL 450M · Q4_K_M")
    ];

    public static VisionModelOption Default => Options[0];

    public static VisionModelOption Resolve(
        AppConfig config,
        IReadOnlyList<VisionModelOption>? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        catalog ??= Options;

        return catalog.FirstOrDefault(model =>
                   model.Repo == config.VisionModelRepo &&
                   model.ModelFile == config.VisionModelFile &&
                   model.ProjectorFile == config.VisionProjectorFile)
               ?? Default;
    }
}
