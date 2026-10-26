using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Core.IO;

public static class PresetThumbnailPixels
{
    public static SKBitmap Render(EditorPreset preset)
    {
        if (preset.Image is { } image) return SKBitmap.Decode(Convert.FromBase64String(image)) ?? throw new InvalidDataException("Invalid preset image.");
        if (preset.Kind == "Shape")
        {
            var result = Bitmaps.Allocate(Bitmaps.ColorInfo(96, 96)); result.Erase(SKColors.Transparent);
            using var path = SKPath.ParseSvgPathData(preset.Path!); using var canvas = new SKCanvas(result); using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            var bounds = path.Bounds; var scale = 88 / Math.Max(1, Math.Max(bounds.Width, bounds.Height)); canvas.Translate(4, 4); canvas.Scale(scale); canvas.Translate(-bounds.Left, -bounds.Top); canvas.DrawPath(path, paint); return result;
        }
        using var doc = new CanvasDocument(Guid.NewGuid(), 280, 65); var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(280, 65)); bitmap.Erase(SKColors.Transparent);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Brush preview"), new(0, 0, 280, 65), "Brush preview"); doc.Layers.Add(layer);
        var brush = preset.Brush ?? new BrushSettings(Hardness: preset.Hardness, Opacity: preset.Opacity);
        var points = Enumerable.Range(0, 41).Select(i => new SKPoint(18 + i * 6, (float)(32 - 9 * Math.Sin(i * Math.PI / 20)))).ToArray();
        var samples = Enumerable.Range(0, 41).Select(i => new PenSample((float)(.3 + .7 * Math.Sin(Math.PI * (i + 1) / 42)))).ToArray();
        BrushEdits.Paint(doc, layer.ID, points, brush with { Diameter = 23, Red = 1, Green = 1, Blue = 1, Seed = 105, Painting = (brush.Painting ?? new()) with { Samples = samples } });
        return layer.Asset!.Image.Copy();
    }
}
