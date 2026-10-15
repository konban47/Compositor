using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Upstream 1.5.3 cubic–arc–cubic corners, with smoothing reduced when adjacent corners meet.</summary>
public static class SmoothCorners
{
    public static SKPath Rectangle(SKRect rect, double radius, bool smooth)
    {
        radius = Math.Clamp(radius, 0, Math.Min(rect.Width, rect.Height) / 2);
        using var path = new SKPathBuilder();
        if (radius <= 0) { path.AddRect(rect); return path.Detach(); }
        var smoothing = smooth ? Math.Min(.6, Math.Max(0, Math.Min(rect.Width, rect.Height) / (2 * radius) - 1)) : 0;
        if (smoothing <= 0) { path.AddRoundRect(rect, (float)radius, (float)radius); return path.Detach(); }
        var reach = (1 + smoothing) * radius; var angle = smoothing * Math.PI / 4;
        var tangent = radius * Math.Tan(angle / 2); var c = tangent * Math.Cos(angle); var d = tangent * Math.Sin(angle);
        var arcLength = radius * Math.Sqrt(2) * Math.Sin((1 - smoothing) * Math.PI / 4); var b = (reach - arcLength - c - d) / 3; var a = 2 * b;
        var corners = new[] { new SKPoint(rect.Right, rect.Top), new SKPoint(rect.Right, rect.Bottom), new SKPoint(rect.Left, rect.Bottom), new SKPoint(rect.Left, rect.Top) };
        for (var i = 0; i < 4; i++)
        {
            var corner = corners[i];
            SKPoint Point(double x, double y) => i switch { 0 => new((float)(corner.X + x), (float)(corner.Y + y)), 1 => new((float)(corner.X - y), (float)(corner.Y + x)), 2 => new((float)(corner.X - x), (float)(corner.Y - y)), _ => new((float)(corner.X + y), (float)(corner.Y - x)) };
            var start = Point(-reach, 0); if (i == 0) path.MoveTo(start); else path.LineTo(start);
            path.CubicTo(Point(-reach + a, 0), Point(-reach + a + b, 0), Point(-reach + a + b + c, d));
            var weight = Math.Cos(Math.PI / 4 - angle); var control = radius / (Math.Sqrt(2) * weight);
            path.ConicTo(Point(-radius + control, radius - control), Point(-radius + radius * Math.Cos(angle), radius - radius * Math.Sin(angle)), (float)weight);
            path.CubicTo(Point(0, reach - a - b), Point(0, reach - a), Point(0, reach));
        }
        path.Close(); return path.Detach();
    }
}
