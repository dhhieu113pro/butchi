using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Butchi.Core.Vision;

namespace Butchi.App.Vision;

public sealed class VisionCaptureWindow : Window
{
    private readonly ScreenCaptureFrame _frame;
    private readonly double _scale;
    private readonly TaskCompletionSource<byte[]?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WriteableBitmap _screenBitmap;
    private readonly Canvas _root;
    private readonly Avalonia.Controls.Shapes.Path _dimPath;
    private readonly Border _selectionBorder;
    private Point _start;
    private Rect _selection;
    private bool _dragging;
    private bool _closed;

    public VisionCaptureWindow(
        ScreenCaptureFrame frame,
        PixelRect screenBounds,
        double scale)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _scale = scale > 0 ? scale : 1;
        if (frame.Width != screenBounds.Width || frame.Height != screenBounds.Height)
            throw new ArgumentException("Captured frame dimensions must match the target screen bounds.", nameof(frame));

        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Background = Brushes.Black;
        Position = new PixelPoint(screenBounds.X, screenBounds.Y);
        Width = screenBounds.Width / _scale;
        Height = screenBounds.Height / _scale;
        Cursor = new Cursor(StandardCursorType.Cross);

        _screenBitmap = CreateBitmap(frame.Width, frame.Height, frame.BgraPixels);
        _root = new Canvas
        {
            Width = Width,
            Height = Height,
            ClipToBounds = true,
            Background = Brushes.Transparent
        };
        var background = new Image
        {
            Source = _screenBitmap,
            Width = Width,
            Height = Height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        _root.Children.Add(background);

        _dimPath = new Avalonia.Controls.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(155, 0, 0, 0)),
            IsHitTestVisible = false
        };
        _root.Children.Add(_dimPath);

        _selectionBorder = new Border
        {
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            IsVisible = false,
            IsHitTestVisible = false
        };
        _root.Children.Add(_selectionBorder);
        Content = _root;

        _root.PointerPressed += OnPointerPressed;
        _root.PointerMoved += OnPointerMoved;
        _root.PointerReleased += OnPointerReleased;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += (_, _) =>
        {
            Activate();
            Focus();
            UpdateSelectionVisuals();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _screenBitmap.Dispose();
            _completion.TrySetResult(null);
        };
    }

    public Task<byte[]?> CaptureAsync()
    {
        if (_closed)
            throw new InvalidOperationException("The screenshot selection window has already been closed.");
        Show();
        return _completion.Task;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root);
        if (point.Properties.IsRightButtonPressed)
        {
            Cancel();
            return;
        }
        if (!point.Properties.IsLeftButtonPressed)
            return;

        _start = e.GetPosition(_root);
        _selection = new Rect(_start, new Size(0, 0));
        _dragging = true;
        e.Pointer.Capture(_root);
        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;

        _selection = ScreenSelectionGeometry.Normalize(_start, e.GetPosition(_root));
        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        e.Pointer.Capture(null);
        _selection = ScreenSelectionGeometry.Normalize(_start, e.GetPosition(_root));
        UpdateSelectionVisuals();
        e.Handled = true;

        if (!ScreenSelectionGeometry.IsUsable(_selection))
        {
            _selection = default;
            UpdateSelectionVisuals();
            return;
        }

        var image = CropSelection(_selection);
        if (image is null)
            return;

        _completion.TrySetResult(image);
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        Cancel();
    }

    private void Cancel()
    {
        _completion.TrySetResult(null);
        Close();
    }

    private void UpdateSelectionVisuals()
    {
        var full = new Rect(0, 0, Width, Height);
        if (ScreenSelectionGeometry.IsUsable(_selection) || _dragging)
        {
            _dimPath.Data = new CombinedGeometry(
                GeometryCombineMode.Xor,
                new RectangleGeometry(full),
                new RectangleGeometry(_selection));
            _selectionBorder.IsVisible = true;
            Canvas.SetLeft(_selectionBorder, _selection.X);
            Canvas.SetTop(_selectionBorder, _selection.Y);
            _selectionBorder.Width = _selection.Width;
            _selectionBorder.Height = _selection.Height;
        }
        else
        {
            _dimPath.Data = new RectangleGeometry(full);
            _selectionBorder.IsVisible = false;
        }
    }

    private byte[]? CropSelection(Rect selection)
    {
        var requested = ScreenSelectionGeometry.ToPixelRect(selection, _scale);
        var left = Math.Clamp(requested.X, 0, _frame.Width);
        var top = Math.Clamp(requested.Y, 0, _frame.Height);
        var right = Math.Clamp(requested.Right, left, _frame.Width);
        var bottom = Math.Clamp(requested.Bottom, top, _frame.Height);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
            return null;

        var rowBytes = checked(width * 4);
        var cropped = new byte[checked(rowBytes * height)];
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                _frame.BgraPixels,
                checked((top + row) * _frame.Stride + left * 4),
                cropped,
                checked(row * rowBytes),
                rowBytes);
        }

        using var bitmap = CreateBitmap(width, height, cropped);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static WriteableBitmap CreateBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        using var framebuffer = bitmap.Lock();
        var sourceStride = checked(width * 4);
        for (var row = 0; row < height; row++)
        {
            Marshal.Copy(
                pixels,
                checked(row * sourceStride),
                IntPtr.Add(framebuffer.Address, checked(row * framebuffer.RowBytes)),
                sourceStride);
        }
        return bitmap;
    }
}
