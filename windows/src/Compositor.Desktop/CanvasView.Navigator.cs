using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.Rendering;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    public bool NavigatorEnabled { get; set; }
    private WriteableBitmap? _navigatorImage;
    private bool _navigatorDirty = true, _navigatorDragging;
    private bool _zoomDragging;
    private bool _dragZoomOut;
    private Point _zoomAnchor;
    internal Rect NavigatorBounds => !NavigatorEnabled || _document is null || Bounds.Width < 220 || Bounds.Height < 170
        ? default : new Rect(Bounds.Width - 200, 16, 184, 132);
    private Rect NavigatorPicture
    {
        get
        {
            var box = NavigatorBounds; if (box.Width == 0 || _document is null) return default;
            var scale = Math.Min((box.Width - 12) / _document.Width, (box.Height - 12) / _document.Height);
            return new Rect(box.Center.X - _document.Width * scale / 2, box.Center.Y - _document.Height * scale / 2,
                _document.Width * scale, _document.Height * scale);
        }
    }
    private void DrawNavigator(DrawingContext context)
    {
        var box = NavigatorBounds; if (box.Width == 0 || _document is null) return;
        if (_navigatorDirty || _navigatorImage is null)
        {
            QueueNavigator(_document);
        }
        context.DrawRectangle(Skin.ChromeBrush, new Pen(Skin.SecondaryBrush, 1), box, 4, 4);
        var picture = NavigatorPicture;
        using (context.PushClip(picture))
        {
            context.DrawRectangle(Paper, null, picture); if (_navigatorImage is not null) context.DrawImage(_navigatorImage, picture);
            var scale = picture.Width / _document.Width;
            DrawNavigatorViewport(context, picture, scale);
        }
    }
    private long _navigatorVersion = -1;
    private CancellationTokenSource? _navigatorCancellation;
    private async void QueueNavigator(CanvasDocument document)
    {
        if (_navigatorVersion == _cpuVersion) return;
        _navigatorCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _navigatorCancellation = cancellation;
        var version = _navigatorVersion = _cpuVersion;
        try
        {
            using var snapshot = new DocumentLease(document);
            using var pixels = await WorkScheduler.Run(token => ViewportRenderer.Render(snapshot.Document,
                SKRectI.Create(document.Width, document.Height), 180, cancellation: token), cancellation.Token);
            if (cancellation.IsCancellationRequested || version != _cpuVersion || !ReferenceEquals(document, _document)) return;
            _navigatorImage?.Dispose(); _navigatorImage = ToImage(pixels); _navigatorDirty = false; base.InvalidateVisual();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!cancellation.IsCancellationRequested) RenderFailed?.Invoke(error.Message); }
        finally { if (ReferenceEquals(_navigatorCancellation, cancellation)) _navigatorCancellation = null; }
    }
    private void NavigateTo(Point point)
    {
        if (_document is null) return;
        var picture = NavigatorPicture; if (picture.Width <= 0) return;
        var x = Math.Clamp((point.X - picture.X) / picture.Width, 0, 1) * _document.Width;
        var y = Math.Clamp((point.Y - picture.Y) / picture.Height, 0, 1) * _document.Height;
        _origin = new SKPoint((float)(x - Bounds.Width / _zoom / 2), (float)(y - Bounds.Height / _zoom / 2)); Moved();
    }
    private bool NavigatorPress(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || NavigatorBounds.Width == 0 || !NavigatorBounds.Contains(e.GetPosition(this))) return false;
        _navigatorDragging = true; NavigateTo(e.GetPosition(this)); e.Pointer.Capture(this); e.Handled = true; return true;
    }
}
