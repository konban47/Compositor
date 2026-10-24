using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    public bool RotateViewEnabled { get; set; }
    public bool ZoomToolEnabled { get; set; }
    public bool ZoomOutMode { get; set; }
    public bool ScrubbyZoom { get; set; } = true;
    private Point? _toolZoomStart;
    private Point _toolZoomLast;
    private bool _toolZoomMoved, _toolZoomOut;
    public void FillScreen()
    {
        if (_document is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        SetZoom(Math.Max(Bounds.Width / _document.Width, Bounds.Height / _document.Height));
        _origin = new SKPoint((float)((_document.Width - Bounds.Width / _zoom) / 2), (float)((_document.Height - Bounds.Height / _zoom) / 2)); Moved();
    }
    public double ViewAngle { get; private set; }
    private bool _drawingScene, _rotatingView;
    private double _rotateStart, _rotateOriginal;
    private Point ViewCenter => new(Bounds.Width / 2, Bounds.Height / 2);
    private Matrix ViewRotation => Matrix.CreateTranslation(-ViewCenter.X, -ViewCenter.Y)
        * Matrix.CreateRotation(ViewAngle * Math.PI / 180) * Matrix.CreateTranslation(ViewCenter.X, ViewCenter.Y);
    private Point Unrotate(Point point) => ViewRotation.Invert().Transform(point);
    public void RotateViewTo(double degrees)
    {
        if (!double.IsFinite(degrees)) return;
        ViewAngle = (degrees % 360 + 360) % 360;
        Moved();
    }
    private static double ViewDistance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private bool ViewToolPress(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _document is null) return false;
        var point = e.GetPosition(this);
        if (ZoomToolEnabled)
        {
            if (e.ClickCount > 1) ActualSize();
            else { _toolZoomStart = _toolZoomLast = point; _toolZoomMoved = false; _toolZoomOut = ZoomOutMode ^ e.KeyModifiers.HasFlag(KeyModifiers.Alt); e.Pointer.Capture(this); }
            e.Handled = true; return true;
        }
        if (!RotateViewEnabled) return false;
        if (e.ClickCount > 1) { RotateViewTo(0); e.Handled = true; return true; }
        _rotateStart = Math.Atan2(point.Y - ViewCenter.Y, point.X - ViewCenter.X);
        _rotateOriginal = ViewAngle; _rotatingView = true;
        e.Pointer.Capture(this); e.Handled = true; return true;
    }
    private bool ViewToolMove(PointerEventArgs e)
    {
        if (_toolZoomStart is { } anchor)
        {
            var now = e.GetPosition(this); _toolZoomMoved |= ViewDistance(now, anchor) > 3;
            if (ScrubbyZoom && _toolZoomMoved) ZoomAt(Math.Pow(2, (now.X - _toolZoomLast.X) / 120), anchor);
            _toolZoomLast = now; e.Handled = true; return true;
        }
        if (!_rotatingView) return false;
        var point = e.GetPosition(this);
        var radians = Math.Atan2(point.Y - ViewCenter.Y, point.X - ViewCenter.X);
        var angle = _rotateOriginal + (radians - _rotateStart) * 180 / Math.PI;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) angle = Math.Round(angle / 15) * 15;
        RotateViewTo(angle); e.Handled = true; return true;
    }
    private bool ViewToolRelease(PointerReleasedEventArgs e)
    {
        if (_toolZoomStart is not { } anchor) return false;
        if (!_toolZoomMoved) ZoomAt(_toolZoomOut ? .8 : 1.25, anchor);
        else if (!ScrubbyZoom)
        {
            var end = e.GetPosition(this); var size = end - anchor;
            var factor = Math.Min(Bounds.Width / Math.Max(2, Math.Abs(size.X)), Bounds.Height / Math.Max(2, Math.Abs(size.Y)));
            var center = new Point((anchor.X + end.X) / 2, (anchor.Y + end.Y) / 2); var at = ToDocument(center);
            SetZoom(_zoom * (_toolZoomOut ? 1 / factor : factor));
            _origin = new((float)(at.X - Bounds.Width / _zoom / 2), (float)(at.Y - Bounds.Height / _zoom / 2)); Moved();
        }
        _toolZoomStart = null; e.Pointer.Capture(null); e.Handled = true; return true;
    }
    private void DrawNavigatorViewport(DrawingContext context, Rect picture, double scale)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            var corners = new[] { new Point(0, 0), new Point(Bounds.Width, 0), new Point(Bounds.Width, Bounds.Height), new Point(0, Bounds.Height) };
            for (var i = 0; i < corners.Length; i++)
            {
                var p = ToDocument(corners[i]); var at = new Point(picture.X + p.X * scale, picture.Y + p.Y * scale);
                if (i == 0) path.BeginFigure(at, false); else path.LineTo(at);
            }
            path.EndFigure(true);
        }
        context.DrawGeometry(null, new Pen(Brushes.White, 3), geometry);
        context.DrawGeometry(null, new Pen(Brushes.Red, 1), geometry);
    }
}
