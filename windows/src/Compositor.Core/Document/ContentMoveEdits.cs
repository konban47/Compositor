using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
namespace Compositor.Core.Document;

public static class ContentMoveEdits
{
    public static (SKBitmap Pixels, SKRectI Region)? Capture(CanvasDocument doc, Guid id, bool allLayers)
    {
        if (doc.Selection.Path is not { IsEmpty: false }) return null;
        var region = doc.Selection.CoverageRect(doc.Width, doc.Height); if (region.IsEmpty) return null;
        using var source = RemoveEdits.Sample(doc, id, region, allLayers); using var mask = doc.Selection.Coverage(region);
        var pixels = Bitmaps.Copy(source);
        for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++)
        { var c = pixels.GetPixel(x, y); var amount = mask is null ? 1 : mask.GetPixelSpan()[y * mask.RowBytes + x] / 255d; pixels.SetPixel(x, y, c.WithAlpha((byte)Math.Round(c.Alpha * amount))); }
        return (pixels, region);
    }
    /// <summary>Move a captured area after its final transform, then heal the original hole in one transaction.</summary>
    public static bool Apply(CanvasDocument doc, Guid id, SKBitmap captured, SKRectI origin, LayerTransform destination, bool extend, bool allLayers, int structure, int color)
    {
        if (!LayerProtection.CanPaint(doc, id) || !LayerProtection.CanMove(doc, id) || doc.Selection.Path is not { IsEmpty: false } path || destination.Width <= 0 || destination.Height <= 0) return false;
        using var rollback = doc.Clone(); var selection = doc.Selection;
        try
        {
            if (!extend && RemoveEdits.Apply(doc, id, path, allLayers, false) is null) return false;
            var layer = doc.Layers.Single(l => l.ID == id); if (layer.Asset is not { } asset) { doc.Adopt(rollback); return false; }
            var local = BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height); if (!local.TryInvert(out var inverse)) { doc.Adopt(rollback); return false; }
            var place = BrushEdits.PixelToDocument(destination, captured.Width, captured.Height);
            var imageMap = SKMatrix.Concat(inverse, place); var wanted = SKRectI.Ceiling(imageMap.MapRect(SKRect.Create(captured.Width, captured.Height)));
            var grid = SKRectI.Union(SKRectI.Create(asset.Width, asset.Height), wanted);
            if (!FilterSurface.Begin(layer, grid, out var working, out var placement)) { doc.Adopt(rollback); return false; }
            using (working)
            using (var patch = Bitmaps.Copy(captured))
            {
                if (color > 0)
                {
                    var target = SKRectI.Intersect(SKRectI.Ceiling(place.MapRect(SKRect.Create(captured.Width, captured.Height))), SKRectI.Create(doc.Width, doc.Height));
                    if (!target.IsEmpty)
                    {
                        using var backdrop = RemoveEdits.Sample(doc, id, target, allLayers); var a = Mean(patch); var b = Mean(backdrop); var amount = Math.Clamp(color, 0, 10) / 10d;
                        for (var y = 0; y < patch.Height; y++) for (var x = 0; x < patch.Width; x++)
                        { var c = patch.GetPixel(x, y); patch.SetPixel(x, y, new SKColor(Byte(c.Red + (b.Red - a.Red) * amount), Byte(c.Green + (b.Green - a.Green) * amount), Byte(c.Blue + (b.Blue - a.Blue) * amount), c.Alpha)); }
                    }
                }
                // High structure retains the boundary; lower values soften a narrow band on its inside.
                var band = (7 - Math.Clamp(structure, 1, 7)) * .5;
                if (band > 0)
                {
                    using var original = Bitmaps.Copy(patch);
                    for (var y = 0; y < patch.Height; y++) for (var x = 0; x < patch.Width; x++)
                    {
                        var c = original.GetPixel(x, y); if (c.Alpha == 0) continue;
                        var distance = Math.Min(Math.Min(x + .5, y + .5), Math.Min(patch.Width - x - .5, patch.Height - y - .5));
                        for (var d = 1; d <= Math.Ceiling(band); d++)
                            if (x - d >= 0 && original.GetPixel(x - d, y).Alpha == 0 || x + d < patch.Width && original.GetPixel(x + d, y).Alpha == 0 || y - d >= 0 && original.GetPixel(x, y - d).Alpha == 0 || y + d < patch.Height && original.GetPixel(x, y + d).Alpha == 0) distance = Math.Min(distance, d - .5);
                        patch.SetPixel(x, y, c.WithAlpha(Byte(c.Alpha * Math.Clamp(distance / band, 0, 1))));
                    }
                }
                using (var canvas = new SKCanvas(working)) { canvas.Translate(-grid.Left, -grid.Top); canvas.Concat(imageMap); canvas.DrawBitmap(patch, new SKPoint(), new SKSamplingOptions(SKFilterMode.Linear)); }
                var maskPosition = layer.MaskTransform;
                FilterSurface.Finish(layer, working, placement);
                var mask = FilterSurface.PlaceMask(layer, maskPosition, placement);
                if (mask is not null) layer.Mask = layer.Mask!.Replacing(ImportedImage.Create(mask, "Mask"));
            }
            var move = SKMatrix.Concat(place, SKMatrix.CreateTranslation(-origin.Left, -origin.Top));
            doc.Selection = selection.Transformed(move, doc.Width, doc.Height); return true;
        }
        catch { doc.Adopt(rollback); throw; }
    }
    private static SKColor Mean(SKBitmap image)
    {
        double r = 0, g = 0, b = 0, count = 0;
        var step = Math.Max(1, (int)Math.Sqrt((long)image.Width * image.Height / 4096d));
        for (var y = 0; y < image.Height; y += step) for (var x = 0; x < image.Width; x += step)
        { var c = image.GetPixel(x, y); var a = c.Alpha / 255d; r += c.Red * a; g += c.Green * a; b += c.Blue * a; count += a; }
        return count <= 0 ? SKColors.Transparent : new SKColor(Byte(r / count), Byte(g / count), Byte(b / count));
    }
    private static byte Byte(double n) => (byte)Math.Clamp(Math.Round(n), 0, 255);
}
