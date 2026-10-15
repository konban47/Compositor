using Avalonia;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    internal CropToolSettings CropSettings { get; set; } = new();
    private Action<SKPoint, SKPoint>? _beforeStraighten;
    private bool _cropStraightening;
    internal void BeginCropStraighten(Action<SKPoint, SKPoint> finish)
    {
        CancelCropStraighten(); _beforeStraighten = UprightDrawn; _cropStraightening = true; UprightDrawing = true;
        UprightDrawn = (a, b) => { CancelCropStraighten(); if (SKPoint.Distance(a, b) > 1) finish(a, b); };
    }
    internal void CancelCropStraighten()
    {
        if (!_cropStraightening) return; _cropStraightening = false; UprightDrawing = false; UprightDrawn = _beforeStraighten; _beforeStraighten = null; _uprightDraft = null; InvalidateVisual();
    }
    private void DrawCropGuides(DrawingContext context, Rect box)
    {
        var c = CropSettings; if (c.OverlayDisplay == 2 || c.OverlayDisplay == 0 && !CropDragging) return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(145, 255, 255, 255)), 1);
        Point At(double x, double y)
        {
            if (c.Orientation is 1 or 2) x = 1 - x; if (c.Orientation is 2 or 3) y = 1 - y;
            return new Point(box.X + x * box.Width, box.Y + y * box.Height);
        }
        void Line(double x, double y, double a, double b) => context.DrawLine(pen, At(x, y), At(a, b));
        switch (c.Overlay)
        {
            case 0: foreach (var t in new[] { 1d / 3, 2d / 3 }) { Line(t, 0, t, 1); Line(0, t, 1, t); } break;
            case 1: for (var i = 1; i < 10; i++) { var t = i / 10d; Line(t, 0, t, 1); Line(0, t, 1, t); } break;
            case 2: Line(0, 0, 1, 1); Line(1, 0, 0, 1); break;
            case 3: Line(0, 1, 1, 0); var r = box.Height * box.Height / (box.Width * box.Width + box.Height * box.Height); Line(0, 0, r, 1 - r); Line(1, 1, 1 - r, r); break;
            case 4: foreach (var t in new[] { .38196601125, .61803398875 }) { Line(t, 0, t, 1); Line(0, t, 1, t); } break;
            case 5:
                Point? previous = null;
                for (var i = 0; i <= 240; i++)
                { var theta = i / 240d * Math.PI * 6; var radius = .6 * Math.Exp(-theta * Math.Log(1.61803398875) / (Math.PI / 2)); var p = At(.618 + radius * Math.Cos(theta + Math.PI), .382 + radius * Math.Sin(theta + Math.PI)); if (previous is { } last && box.Contains(last) && box.Contains(p)) context.DrawLine(pen, last, p); previous = p; } break;
        }
    }
}
