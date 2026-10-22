using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public enum SelectionPaintMode { New, Add, Subtract, Intersect }
public static class SelectionBrushEdits
{
    public static bool Paint(CanvasDocument doc, IReadOnlyList<SKPoint> points, double diameter, double opacity, SelectionPaintMode mode, double hardness = 1)
    {
        if (points.Count == 0 || diameter <= 0 || (long)doc.Width * doc.Height > DocumentLimits.MaxSurfacePixels) return false;
        var radius = (float)diameter / 2;
        var left = Math.Max(0, (int)Math.Floor(points.Min(p => p.X) - radius - 1));
        var top = Math.Max(0, (int)Math.Floor(points.Min(p => p.Y) - radius - 1));
        var right = Math.Min(doc.Width, (int)Math.Ceiling(points.Max(p => p.X) + radius + 1));
        var bottom = Math.Min(doc.Height, (int)Math.Ceiling(points.Max(p => p.Y) + radius + 1));
        var area = SKRectI.Create(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(Math.Max(1, area.Width), Math.Max(1, area.Height))); mask.Erase(SKColors.Black);
        using (var canvas = new SKCanvas(mask))
        using (var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Lighten })
        {
            canvas.Translate(-left, -top);
            var hard = (float)Math.Clamp(hardness, 0, .9999);
            void Stamp(SKPoint point)
            {
                using var shader = SKShader.CreateRadialGradient(point, radius, [SKColors.White, SKColors.White, SKColors.Black], [0, hard, 1], SKShaderTileMode.Clamp);
                paint.Shader = hardness < 1 ? shader : null; paint.Color = SKColors.White; canvas.DrawCircle(point, radius, paint);
            }
            Stamp(points[0]);
            for (var i = 1; i < points.Count; i++)
            {
                var count = Math.Max(1, (int)Math.Ceiling(SKPoint.Distance(points[i - 1], points[i]) / Math.Max(1, diameter * .08)));
                for (var j = 1; j <= count; j++) Stamp(points[i - 1] + PlacementMesh.Scale(points[i] - points[i - 1], (float)j / count));
            }
        }
        return Combine(doc, mask, opacity, mode, new SKPointI(left, top));
    }
    public static bool Quick(CanvasDocument doc, Guid? sampleLayer, IReadOnlyList<SKPoint> points, double diameter, int tolerance, SelectionPaintMode mode, double angle = 0, bool enhanceEdge = false, double hardness = 1)
    {
        if (points.Count == 0) return false;
        using var image = SelectionEdits.Sample(doc, sampleLayer); if (image is null) return false;
        using var result = doc.Clone(); result.Selection = DocumentSelection.All;
        // Seed along the complete gesture, including fast pointer moves, then union edge-bounded color regions.
        var seeds = new List<SKPoint> { points[0] };
        for (var i = 1; i < points.Count; i++)
        { var n = Math.Clamp((int)Math.Ceiling(SKPoint.Distance(points[i - 1], points[i]) / Math.Max(1, diameter / 3)), 1, 64); for (var j = 1; j <= n; j++) seeds.Add(points[i - 1] + PlacementMesh.Scale(points[i] - points[i - 1], (float)j / n)); }
        var radians = angle * Math.PI / 180; var dx = new SKPoint((float)(Math.Cos(radians) * diameter / 3), (float)(Math.Sin(radians) * diameter / 3)); var dy = new SKPoint(-dx.Y, dx.X);
        var stride = Math.Max(1, (int)Math.Ceiling(seeds.Count / 128.0));
        foreach (var p in seeds.Where((_, i) => i % stride == 0))
            foreach (var seed in new[] { p, p + dx, p - dx, p + dy, p - dy })
                if (result.Selection.Path?.Contains(seed.X, seed.Y) != true)
                    SelectionEdits.SelectWand(result, image, (int)seed.X, (int)seed.Y, new WandOptions(0, tolerance, true), SelectionMode.Add);
        if (result.Selection.Path is null) return false;
        var mask = result.Selection.WithFeather((1 - Math.Clamp(hardness, 0, 1)) * diameter * .1).Coverage(SKRectI.Create(doc.Width, doc.Height));
        if (mask is null) return false;
        if (enhanceEdge)
        {
            using var source = mask.Copy();
            for (var y = 1; y < mask.Height - 1; y++) for (var x = 1; x < mask.Width - 1; x++)
            { var center = image.GetPixel(x, y); double sum = 0, weight = 0;
                for (var j = -1; j <= 1; j++) for (var i = -1; i <= 1; i++)
                { var other = image.GetPixel(x + i, y + j); var difference = Math.Abs(center.Red - other.Red) + Math.Abs(center.Green - other.Green) + Math.Abs(center.Blue - other.Blue); var w = 1 / (1 + difference * difference / 900.0); sum += source.GetPixel(x + i, y + j).Red * w; weight += w; }
                var v = (byte)Math.Round(sum / weight); mask.SetPixel(x, y, new SKColor(v, v, v));
            }
        }
        return Combine(doc, mask, 1, mode);
    }
    public static bool Combine(CanvasDocument doc, SKBitmap mask, double opacity, SelectionPaintMode mode, SKPointI origin = default)
    {
        if (mask.ColorType != SKColorType.Gray8)
        {
            var gray = Bitmaps.Allocate(Bitmaps.MaskInfo(mask.Width, mask.Height));
            var bytes = gray.GetPixelSpan();
            for (var y = 0; y < mask.Height; y++) for (var x = 0; x < mask.Width; x++) bytes[y * gray.RowBytes + x] = mask.GetPixel(x, y).Red;
            mask.Dispose(); mask = gray;
        }
        var incoming = SKRectI.Intersect(SKRectI.Create(origin.X, origin.Y, mask.Width, mask.Height), SKRectI.Create(doc.Width, doc.Height));
        var old = doc.Selection.CoverageRect(doc.Width, doc.Height);
        var area = mode switch
        {
            SelectionPaintMode.Add => old.IsEmpty ? incoming : incoming.IsEmpty ? old : SKRectI.Union(old, incoming),
            SelectionPaintMode.Subtract => old,
            SelectionPaintMode.Intersect => SKRectI.Intersect(old, incoming),
            _ => incoming,
        };
        if (area.IsEmpty)
        {
            mask.Dispose(); doc.Selection = DocumentSelection.FromPath(new SKPath()); return true;
        }
        using var previous = mode == SelectionPaintMode.New || doc.Selection.Path is null ? null : doc.Selection.Coverage(area);
        var output = mask;
        if (area.Left != origin.X || area.Top != origin.Y || area.Width != mask.Width || area.Height != mask.Height || mask.ColorType != SKColorType.Gray8)
        {
            output = Bitmaps.Allocate(Bitmaps.MaskInfo(area.Width, area.Height)); output.Erase(SKColors.Black);
            using (var canvas = new SKCanvas(output)) canvas.DrawBitmap(mask, new SKPoint(origin.X - area.Left, origin.Y - area.Top), new SKSamplingOptions(SKFilterMode.Nearest));
            mask.Dispose();
        }
        var pixels = output.GetPixelSpan();
        var before = previous is null ? ReadOnlySpan<byte>.Empty : previous.GetPixelSpan();
        var strength = Math.Clamp(opacity, 0, 1);
        for (var y = 0; y < area.Height; y++) for (var x = 0; x < area.Width; x++)
        {
            var at = y * output.RowBytes + x;
            var a = previous is null ? 0 : before[y * previous.RowBytes + x] / 255.0;
            var b = pixels[at] / 255.0 * strength;
            var value = mode switch { SelectionPaintMode.Add => a + b - a * b, SelectionPaintMode.Subtract => a * (1 - b), SelectionPaintMode.Intersect => a * b, _ => b };
            pixels[at] = (byte)Math.Round(value * 255);
        }
        doc.Selection = DocumentSelection.FromCoverage(output, new SKPoint(area.Left, area.Top)); return true;
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
