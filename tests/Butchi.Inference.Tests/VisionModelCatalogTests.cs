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
}
