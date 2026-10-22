using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Bounded repair patches shared by the remove tool and generated replacements.</summary>
public static class RemoveEdits
{
    public static SKRectI Region(CanvasDocument doc, SKPath area)
    {
        var bounds = SKRectI.Ceiling(area.Bounds);
        var reach = Math.Min(512, Math.Max(32, Math.Max(bounds.Width, bounds.Height)));
        return SKRectI.Intersect(SKRectI.Create(bounds.Left - reach, bounds.Top - reach, bounds.Width + reach * 2, bounds.Height + reach * 2), SKRectI.Create(doc.Width, doc.Height));
    }
    public static SKBitmap Sample(CanvasDocument doc, Guid id, SKRectI region, bool all)
    {
        var result = Bitmaps.Allocate(Bitmaps.ColorInfo(region.Width, region.Height));
        try
        {
            using var canvas = new SKCanvas(result); canvas.Clear(SKColors.Transparent); canvas.Translate(-region.Left, -region.Top);
            if (all) { using var rendered = DocumentRenderer.RenderRegion(doc, region); canvas.DrawBitmap(rendered, new SKPoint(region.Left, region.Top), new SKSamplingOptions(SKFilterMode.Nearest)); }
            else if (doc.Layers.FirstOrDefault(l => l.ID == id) is { } layer) DocumentRenderer.DrawLayerPixels(canvas, layer);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    public static Guid? Apply(CanvasDocument doc, Guid id, SKPath area, bool all, bool newLayer, SKBitmap? generated = null, uint seed = 0)
    {
        if (area.IsEmpty || doc.Layers.FirstOrDefault(l => l.ID == id) is not { IsGroup: false } layer
            || !newLayer && !LayerProtection.CanPaint(doc, id)) return null;
        var region = Region(doc, area); if (region.IsEmpty) return null;
        using var source = Sample(doc, id, region, all);
        using var output = generated is null ? Bitmaps.Copy(source) : generated.Resize(Bitmaps.ColorInfo(region.Width, region.Height), new SKSamplingOptions(SKFilterMode.Linear));
        if (output is null) return null;
        using var mask = Bitmaps.Allocate(Bitmaps.MaskInfo(region.Width, region.Height));
        using (var canvas = new SKCanvas(mask)) { canvas.Clear(SKColors.Black); canvas.Translate(-region.Left, -region.Top); using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true }; canvas.DrawPath(area, paint); }
        using var selected = doc.Selection.Coverage(region);
        var coverage = MemoryBudget.Allocate<float>(checked(region.Width * region.Height)); var any = false;
        for (var y = 0; y < region.Height; y++) for (var x = 0; x < region.Width; x++)
        { var amount = mask.GetPixelSpan()[y * mask.RowBytes + x] / 255f * (selected is null ? 1 : selected.GetPixelSpan()[y * selected.RowBytes + x] / 255f); coverage[y * region.Width + x] = amount; any |= amount > 0; }
        if (!any) return null;
        if (generated is null)
        {
            var full = MemoryBudget.Allocate<float>(coverage.Length); for (var i = 0; i < coverage.Length; i++) full[i] = coverage[i] > 0 ? 1 : 0;
            if (!BrushEdits.Heal(output, full, new BrushSettings(Mode: BrushMode.Heal, Healing: HealingMode.ContentAware, Seed: seed))) return null;
        }
        if (newLayer)
        {
            var patch = Bitmaps.Copy(output);
            for (var y = 0; y < patch.Height; y++) for (var x = 0; x < patch.Width; x++)
            { var c = patch.GetPixel(x, y); patch.SetPixel(x, y, c.WithAlpha((byte)Math.Round(c.Alpha * coverage[y * patch.Width + x]))); }
            try { var made = LayerPlacement.AddImage(doc, patch, region, "Remove", id); if (made is null) patch.Dispose(); return made; }
            catch { patch.Dispose(); throw; }
        }
        BrushEdits.EnsurePixels(doc, id); if (layer.Asset is not { } asset) return null;
        var pixels = Bitmaps.Copy(asset.Image); var map = BrushEdits.PixelToDocument(layer.Transform, pixels.Width, pixels.Height);
        for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++)
        {
            var at = map.MapPoint(x + .5f, y + .5f); var column = (int)Math.Floor(at.X) - region.Left; var row = (int)Math.Floor(at.Y) - region.Top;
            if (column < 0 || row < 0 || column >= region.Width || row >= region.Height) continue;
            var amount = coverage[row * region.Width + column]; if (amount <= 0) continue;
            var c = output.GetPixel(column, row); var old = pixels.GetPixel(x, y);
            pixels.SetPixel(x, y, RetouchBrush.Mix(old, c, amount));
        }
        try { layer.Asset = ImportedImage.Create(pixels, asset.Name); return id; }
        catch { pixels.Dispose(); throw; }
    }
}
