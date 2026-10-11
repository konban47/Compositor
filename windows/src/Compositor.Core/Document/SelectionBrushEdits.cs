using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum SelectionPaintMode { New, Add, Subtract, Intersect }
public static class SelectionBrushEdits
{
    public static bool Paint(CanvasDocument doc, IReadOnlyList<SKPoint> points, double diameter, double opacity, SelectionPaintMode mode)
    {
        if (points.Count == 0 || diameter <= 0 || (long)doc.Width * doc.Height > DocumentLimits.MaxSurfacePixels) return false;
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(doc.Width, doc.Height)); mask.Erase(SKColors.Black);
        using (var canvas = new SKCanvas(mask))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true, StrokeWidth = (float)diameter, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Style = SKPaintStyle.Stroke })
        {
            if (points.Count == 1) { paint.Style = SKPaintStyle.Fill; canvas.DrawCircle(points[0], (float)diameter / 2, paint); }
            else { using var builder = new SKPathBuilder(); builder.MoveTo(points[0]); foreach (var p in points.Skip(1)) builder.LineTo(p); using var path = builder.Detach(); canvas.DrawPath(path, paint); }
        }
        return Combine(doc, mask, opacity, mode);
    }
    public static bool Quick(CanvasDocument doc, Guid? sampleLayer, IReadOnlyList<SKPoint> points, double diameter, int tolerance, SelectionPaintMode mode)
    {
        if (points.Count == 0) return false;
        using var image = SelectionEdits.Sample(doc, sampleLayer); if (image is null) return false;
        using var result = doc.Clone(); result.Selection = DocumentSelection.All;
        // Seed along the complete gesture, including fast pointer moves, then union edge-bounded color regions.
        var seeds = new List<SKPoint> { points[0] };
        for (var i = 1; i < points.Count; i++)
        { var n = Math.Clamp((int)Math.Ceiling(SKPoint.Distance(points[i - 1], points[i]) / Math.Max(1, diameter / 3)), 1, 64); for (var j = 1; j <= n; j++) seeds.Add(points[i - 1] + PlacementMesh.Scale(points[i] - points[i - 1], (float)j / n)); }
        var stride = Math.Max(1, (int)Math.Ceiling(seeds.Count / 128.0));
        foreach (var p in seeds.Where((_, i) => i % stride == 0))
            foreach (var seed in new[] { p, p + new SKPoint((float)diameter / 3, 0), p - new SKPoint((float)diameter / 3, 0), p + new SKPoint(0, (float)diameter / 3), p - new SKPoint(0, (float)diameter / 3) })
                if (result.Selection.Path?.Contains(seed.X, seed.Y) != true)
                    SelectionEdits.SelectWand(result, image, (int)seed.X, (int)seed.Y, new WandOptions(0, tolerance, true), SelectionMode.Add);
        if (result.Selection.Path is null) return false;
        var mask = result.Selection.Coverage(SKRectI.Create(doc.Width, doc.Height));
        return mask is not null && Combine(doc, mask, 1, mode);
    }
    private static bool Combine(CanvasDocument doc, SKBitmap mask, double opacity, SelectionPaintMode mode)
    {
        using var previous = doc.Selection.Path is null ? null : doc.Selection.Coverage(SKRectI.Create(doc.Width, doc.Height));
        for (var y = 0; y < doc.Height; y++) for (var x = 0; x < doc.Width; x++)
        {
            var a = (previous?.GetPixel(x, y).Red ?? 0) / 255.0; var b = mask.GetPixel(x, y).Red / 255.0 * Math.Clamp(opacity, 0, 1);
            var value = mode switch { SelectionPaintMode.Add => a + b - a * b, SelectionPaintMode.Subtract => a * (1 - b), SelectionPaintMode.Intersect => a * b, _ => b };
            var c = (byte)Math.Round(value * 255); mask.SetPixel(x, y, new SKColor(c, c, c));
        }
        doc.Selection = DocumentSelection.FromCoverage(mask); return true;
    }
    public static SKPoint SnapEdge(SKBitmap image, SKPoint point, double radius, double contrast)
    {
        var cx = (int)point.X; var cy = (int)point.Y; var r = Math.Clamp((int)radius, 1, 64); var best = point; double score = contrast;
        double Light(int x, int y) { var c = image.GetPixel(Math.Clamp(x, 0, image.Width - 1), Math.Clamp(y, 0, image.Height - 1)); return (c.Red * .2126 + c.Green * .7152 + c.Blue * .0722) * c.Alpha / 255; }
        for (var y = Math.Max(1, cy - r); y < Math.Min(image.Height - 1, cy + r); y++) for (var x = Math.Max(1, cx - r); x < Math.Min(image.Width - 1, cx + r); x++)
        {
            var distance = Math.Sqrt((x - point.X) * (x - point.X) + (y - point.Y) * (y - point.Y)); if (distance > r) continue;
            var edge = Math.Sqrt(Math.Pow(Light(x + 1, y) - Light(x - 1, y), 2) + Math.Pow(Light(x, y + 1) - Light(x, y - 1), 2));
            var candidate = edge * (1 - .75 * distance / r); if (candidate > score) { score = candidate; best = new SKPoint(x, y); }
        }
        return best;
    }
}
