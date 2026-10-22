using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Upstream line patterns measured from the beginning of the first cap, in stroke widths.</summary>
public static class ShapeLineStroke
{
    public static float Reach(SKPoint a, SKPoint b, float width, string cap)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y; var length = MathF.Sqrt(dx * dx + dy * dy);
        return cap != "Square" || length == 0 ? width / 2
            : width / 2 + MathF.Ceiling(width / 2 * (MathF.Abs(dx) + MathF.Abs(dy)) / length - width / 2 - .0001f);
    }
    internal static void Draw(SKCanvas canvas, SKPoint a, SKPoint b, SKPaint paint, LayerShapeStyle style)
    {
        // Leave the rasterization of older projects unchanged when their optional fields are absent.
        if (style.LineStyle is null && style.LineCap is null) { canvas.DrawLine(a, b, paint); return; }
        using var outline = Outline(a, b, paint.StrokeWidth, style);
        using var fill = new SKPaint { Color = paint.Color, IsAntialias = paint.IsAntialias };
        canvas.DrawPath(outline, fill);
    }
    internal static SKPath Outline(SKPoint a, SKPoint b, float width, LayerShapeStyle style)
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round };
        using var lines = new SKPathBuilder(); using var filled = new SKPathBuilder();
        void Line(SKPoint from, SKPoint to) { lines.MoveTo(from); lines.LineTo(to); }
        SKPath Finish()
        {
            using var strokes = lines.Detach(); paint.GetFillPath(strokes, filled); return filled.Detach();
        }
        if (MathF.Abs(width - MathF.Round(width)) < .0001f)
        {
            var offset = ((int)MathF.Round(width) & 1) * .5f;
            if (MathF.Abs(a.Y - b.Y) < .0001f) { a.Y = b.Y = MathF.Floor(a.Y) + offset; a.X = MathF.Floor(a.X) + offset; b.X = MathF.Floor(b.X) + offset; }
            if (MathF.Abs(a.X - b.X) < .0001f) { a.X = b.X = MathF.Floor(a.X) + offset; a.Y = MathF.Floor(a.Y) + offset; b.Y = MathF.Floor(b.Y) + offset; }
        }
        var delta = b - a; var length = delta.Length;
        var direction = length > 0 ? new SKPoint(delta.X / length, delta.Y / length) : new SKPoint(1, 0);
        SKPoint Along(float distance) => a + new SKPoint(direction.X * (distance - width / 2), direction.Y * (distance - width / 2));
        var dash = style.LineStyle switch { "Dotted" => 1, "Dashed" => 3, "Long Dash" => 7, _ => 0 };
        var gap = style.LineStyle == "Dotted" ? 1 : 2;
        var total = length + width;
        if ((style.LineCap ?? "Round") == "Square")
        {
            paint.StrokeCap = SKStrokeCap.Butt;
            using var effect = dash > 0 ? SKPathEffect.CreateDash([dash * width, gap * width], 0) : null;
            paint.PathEffect = effect; Line(Along(0), Along(total)); return Finish();
        }
        paint.StrokeCap = SKStrokeCap.Round;
        if (dash == 0 && length > 0) { Line(a, b); return Finish(); }
        if (dash == 0) { filled.AddCircle(a.X, a.Y, width / 2); return filled.Detach(); }
        using var dots = new SKPathBuilder();
        for (float distance = 0; distance < total; distance += (dash + gap) * width)
        {
            var end = Math.Min(total, distance + dash * width);
            if (end - distance - width > .001f) Line(Along(distance + width / 2), Along(end - width / 2));
            else { var middle = Along((distance + end) / 2); dots.AddCircle(middle.X, middle.Y, width / 2); }
        }
        using var strokePath = Finish(); filled.AddPath(strokePath);
        using var dotPath = dots.Detach(); filled.AddPath(dotPath); return filled.Detach();
    }
}
