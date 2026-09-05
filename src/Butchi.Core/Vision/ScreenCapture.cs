namespace Butchi.Core.Vision;

public sealed record ScreenCaptureBounds(int X, int Y, int Width, int Height);

public sealed record ScreenCaptureFrame(int Width, int Height, byte[] BgraPixels)
{
    public int Stride => checked(Width * 4);
}

public interface IScreenCaptureService
{
    ValueTask<ScreenCaptureFrame> CaptureAsync(
        ScreenCaptureBounds bounds,
        CancellationToken cancellationToken);
}
