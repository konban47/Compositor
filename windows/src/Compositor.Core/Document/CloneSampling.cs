using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;
namespace Compositor.Core.Document;

/// <summary>Samples only the source footprint needed by a stroke, including the healing neighborhood.</summary>
public static class CloneSampling
{
    public static SKMatrix Mapping(SKPoint source, SKPoint anchor, double width, double height, double angle, bool flipX, bool flipY)
    {
        var radians = -angle * Math.PI / 180; var x = (flipX ? -1 : 1) / Math.Clamp(width / 100, .01, 10); var y = (flipY ? -1 : 1) / Math.Clamp(height / 100, .01, 10);
        var a = (float)(Math.Cos(radians) * x); var b = (float)(-Math.Sin(radians) * x);
        var c = (float)(Math.Sin(radians) * y); var d = (float)(Math.Cos(radians) * y);
        return new SKMatrix(a, b, source.X - a * anchor.X - b * anchor.Y, c, d, source.Y - c * anchor.X - d * anchor.Y, 0, 0, 1);
    }
    internal static SKBitmap Read(CanvasDocument destination, Guid layerID, IReadOnlyList<SKPoint> points, BrushSettings settings, out BrushSettings adjusted)
    {
        var doc = settings.CloneDocument ?? destination; var id = settings.CloneLayerID ?? layerID;
        var bounds = new SKRect(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
        bounds.Inflate((float)settings.Diameter / 2 + 2, (float)settings.Diameter / 2 + 2);
        var offset = settings.CloneFrom ?? default; var map = settings.CloneMapping ?? SKMatrix.CreateTranslation(offset.X, offset.Y);
        var mapped = map.MapRect(bounds); mapped.Inflate(18, 18);
        var region = SKRectI.Intersect(SKRectI.Ceiling(mapped), SKRectI.Create(doc.Width, doc.Height));
        if (region.IsEmpty) region = new SKRectI(0, 0, 1, 1);
        adjusted = settings with { SampleOrigin = new(region.Left, region.Top) };
        if (settings.HealingPattern)
        {
            var result = DocumentRenderer.Allocate(region.Width, region.Height);
            for (var y = 0; y < result.Height; y++) for (var x = 0; x < result.Width; x++)
            {
                var px = x + region.Left; var py = y + region.Top;
                var color = settings.Pattern is { } tile ? tile.GetPixel(px % tile.Width, py % tile.Height) : ((px / 8 + py / 8) % 2 == 0 ? SKColors.Black : SKColors.White);
                result.SetPixel(x, y, color);
            }
            return result;
        }
        return Render(doc, id, settings.CloneAllLayers ? 2 : settings.CloneScope, settings.CloneIgnoreAdjustments, region);
    }
    public static SKBitmap Render(CanvasDocument doc, Guid id, int scope, bool ignoreAdjustments, SKRectI region)
    {
        if (scope == 0)
        {
            var result = DocumentRenderer.Allocate(region.Width, region.Height);
            using var canvas = new SKCanvas(result); canvas.Clear(SKColors.Transparent); canvas.Translate(-region.Left, -region.Top);
            if (doc.Layers.FirstOrDefault(l => l.ID == id) is { } layer) DocumentRenderer.DrawLayerPixels(canvas, layer);
            return result;
        }
        using var view = ColorSampling.View(doc, id, scope == 1 ? (ignoreAdjustments ? SampleScope.CurrentAndBelowWithoutAdjustments : SampleScope.CurrentAndBelow) : (ignoreAdjustments ? SampleScope.AllWithoutAdjustments : SampleScope.AllLayers));
        return DocumentRenderer.RenderRegion(view, region);
    }
}
