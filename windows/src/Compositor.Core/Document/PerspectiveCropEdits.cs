using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class PerspectiveCropEdits
{
    public static SKMatrix? UnitToQuad(IReadOnlyList<SKPoint> p)
    {
        if (p.Count != 4 || !DistortWarp.IsConvex(p)) return null;
        var dx1 = p[1].X - p[2].X; var dx2 = p[3].X - p[2].X; var dx3 = p[0].X - p[1].X + p[2].X - p[3].X;
        var dy1 = p[1].Y - p[2].Y; var dy2 = p[3].Y - p[2].Y; var dy3 = p[0].Y - p[1].Y + p[2].Y - p[3].Y;
        var determinant = dx1 * dy2 - dx2 * dy1; if (Math.Abs(determinant) < .00001) return null;
        var g = (dx3 * dy2 - dx2 * dy3) / determinant; var h = (dx1 * dy3 - dx3 * dy1) / determinant;
        return new SKMatrix(p[1].X - p[0].X + g * p[1].X, p[3].X - p[0].X + h * p[3].X, p[0].X,
            p[1].Y - p[0].Y + g * p[1].Y, p[3].Y - p[0].Y + h * p[3].Y, p[0].Y, g, h, 1);
    }
    public static bool Apply(CanvasDocument doc, IReadOnlyList<SKPoint> quad, int width, int height)
    {
        if (width is < 1 or > DocumentLimits.MaxSide || height is < 1 or > DocumentLimits.MaxSide || UnitToQuad(quad) is not { } source
            || (long)width * height > DocumentLimits.MaxSurfacePixels
            || (long)width * height * Math.Max(1, doc.Layers.Count(l => l.Asset is not null) + doc.Layers.Count(l => l.Mask is not null) + doc.Channels.Count) > DocumentLimits.DocumentPixelBudget) return false;
        if (!source.TryInvert(out var forward)) return false;
        var made = new List<(ImageLayer Layer, SKBitmap? Image, SKBitmap? Mask)>(); var channels = new List<(AlphaChannel Channel, SKBitmap Image)>();
        var whole = new LayerTransform(0, 0, width, height);
        SKBitmap Raster(SKBitmap image, LayerTransform transform, bool gray)
        {
            var into = Bitmaps.Allocate(gray ? Bitmaps.MaskInfo(width, height) : Bitmaps.ColorInfo(width, height)); into.Erase(gray ? SKColors.Black : SKColors.Transparent);
            // Skia maps source pixels directly into the crop using the same projective transform as the four controls.
            var pixel = SKMatrix.Concat(SKMatrix.CreateScale(width, height), SKMatrix.Concat(forward, BrushEdits.PixelToDocument(transform, image.Width, image.Height)));
            using var canvas = new SKCanvas(into); canvas.SetMatrix(pixel);
            using var paint = new SKPaint { IsAntialias = true }; canvas.DrawBitmap(image, new SKPoint(), new SKSamplingOptions(SKFilterMode.Linear), paint);
            return into;
        }
        SKBitmap Mask(ImageLayer layer)
        {
            var mask = layer.Mask!;
            if (mask.VectorPath is null) return Raster(mask.Asset.Image, layer.MaskTransform, true);
            var raw = mask.Clone(); raw.Density = 1; raw.Feather = 0;
            using var coverage = MaskProperties.Coverage(raw, layer.MaskTransform, layer.MaskTransform, mask.Asset.Width, mask.Asset.Height);
            return Raster(coverage, layer.MaskTransform, true);
        }
        try
        {
            foreach (var layer in doc.Layers) made.Add((layer, layer.Asset is { } a ? Raster(a.Image, layer.Transform, false) : null, layer.Mask is not null ? Mask(layer) : null));
            foreach (var channel in doc.Channels) channels.Add((channel, Raster(channel.Asset.Image, new LayerTransform(0, 0, doc.Width, doc.Height), true)));
        }
        catch { foreach (var item in made) { item.Image?.Dispose(); item.Mask?.Dispose(); } foreach (var item in channels) item.Image.Dispose(); throw; }
        foreach (var (layer, image, mask) in made)
        {
            if (image is not null) { layer.Asset = ImportedImage.Create(image, layer.Name); layer.Shape = null; layer.Text = null; layer.SmartObject = null; }
            if (mask is not null) { var prior = layer.Mask!; layer.Mask = LayerMask.AssetFrom(mask); layer.Mask.IsEnabled = prior.IsEnabled; layer.Mask.IsLinked = prior.IsLinked; layer.Mask.Density = prior.Density; layer.Mask.Feather = prior.Feather; layer.Mask.Placement = whole; }
            layer.Transform = whole;
        }
        DocumentMarks.Transform(doc, SKMatrix.Concat(SKMatrix.CreateScale(width, height), forward));
        doc.Channels.Clear(); foreach (var (channel, image) in channels) doc.Channels.Add(channel with { Asset = ImportedImage.Create(image, channel.Name) });
        doc.Guides.Clear(); doc.Selection = DocumentSelection.All; doc.Width = width; doc.Height = height; return true;
    }
}
