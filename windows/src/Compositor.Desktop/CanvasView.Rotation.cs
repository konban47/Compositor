using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    public bool RotateViewEnabled { get; set; }
    public bool ZoomToolEnabled { get; set; }
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
    private bool ViewToolPress(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _document is null) return false;
        var point = e.GetPosition(this);
        if (ZoomToolEnabled)
        {
            if (e.ClickCount > 1) ActualSize();
            else ZoomAt(e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 1 / 1.25 : 1.25, point);
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
        if (!_rotatingView) return false;
        var point = e.GetPosition(this);
        var radians = Math.Atan2(point.Y - ViewCenter.Y, point.X - ViewCenter.X);
        var angle = _rotateOriginal + (radians - _rotateStart) * 180 / Math.PI;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) angle = Math.Round(angle / 15) * 15;
        RotateViewTo(angle); e.Handled = true; return true;
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
