using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    public bool NavigatorEnabled { get; set; }
    private WriteableBitmap? _navigatorImage;
    private bool _navigatorDirty = true, _navigatorDragging;
    private bool _zoomDragging;
    private Point _zoomAnchor;
    internal Rect NavigatorBounds => !NavigatorEnabled || _zoom < 3 || _document is null || Bounds.Width < 220 || Bounds.Height < 170
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
            using var pixels = DocumentRenderer.Preview(_document, 180);
            _navigatorImage?.Dispose(); _navigatorImage = ToImage(pixels); _navigatorDirty = false;
        }
        context.DrawRectangle(Skin.ChromeBrush, new Pen(Skin.SecondaryBrush, 1), box, 4, 4);
        var picture = NavigatorPicture;
        using (context.PushClip(picture))
        {
            context.DrawRectangle(Paper, null, picture); context.DrawImage(_navigatorImage, picture);
            var scale = picture.Width / _document.Width;
            var view = new Rect(picture.X + _origin.X * scale, picture.Y + _origin.Y * scale,
                Bounds.Width / _zoom * scale, Bounds.Height / _zoom * scale);
            context.DrawRectangle(null, new Pen(Brushes.White, 3), view);
            context.DrawRectangle(null, new Pen(Brushes.Red, 1), view);
        }
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
