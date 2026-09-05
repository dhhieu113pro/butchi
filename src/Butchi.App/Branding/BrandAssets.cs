using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Butchi.App.Branding;

public static class BrandAssets
{
    private static readonly Uri WindowIconUri = new("avares://Butchi.App/!__AvaloniaDefaultWindowIcon");
    private static readonly Uri LogoUri = new("avares://Butchi.App/Assets/ButchiLogo.png");

    public static WindowIcon CreateWindowIcon()
    {
        using var stream = AssetLoader.Open(WindowIconUri);
        return new WindowIcon(stream);
    }

    public static Bitmap CreateBitmap()
    {
        using var stream = AssetLoader.Open(LogoUri);
        return new Bitmap(stream);
    }
}
