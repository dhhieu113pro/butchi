using Butchi.Core.Configuration;
using Xunit;

namespace Butchi.Inference.Tests;

public sealed class VisionModelCatalogTests
{
    [Fact]
    public void Default_uses_the_verified_LFM25_VL_GGUF_pair()
    {
        var model = VisionModelCatalog.Default;

        Assert.Equal("LiquidAI/LFM2.5-VL-450M-GGUF", model.Repo);
        Assert.Equal("LFM2.5-VL-450M-Q4_K_M.gguf", model.ModelFile);
        Assert.Equal("mmproj-LFM2.5-VL-450m-Q8_0.gguf", model.ProjectorFile);
    }

    [Fact]
    public void Resolve_uses_the_configured_catalog_vision_model()
    {
        var configured = new VisionModelOption(
            "example/vision",
            "vision.gguf",
            "mmproj.gguf",
            "Example Vision");
        var catalog = new[] { VisionModelCatalog.Default, configured };
        var config = AppConfig.Default with
        {
            VisionModelRepo = configured.Repo,
            VisionModelFile = configured.ModelFile,
            VisionProjectorFile = configured.ProjectorFile
        };

        var selected = VisionModelCatalog.Resolve(config, catalog);

        Assert.Equal(configured, selected);
    }
}
