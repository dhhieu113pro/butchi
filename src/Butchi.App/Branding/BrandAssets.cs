using Avalonia.Controls;
using Avalonia.Platform;

namespace Butchi.App.Branding;

public static class BrandAssets
{
    private static readonly Uri LogoUri = new("avares://Butchi.App/Assets/ButchiLogo.png");

    public static WindowIcon CreateWindowIcon()
    {
        using var stream = AssetLoader.Open(LogoUri);
        return new WindowIcon(stream);
    }
}
