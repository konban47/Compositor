using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Desktop;
internal sealed class BrushStrokePreview(int index) : Control
{
    private static readonly Dictionary<int, Avalonia.Media.Imaging.Bitmap> Pictures = [];
    public override void Render(DrawingContext context)
    {
        if (!Pictures.TryGetValue(index, out var picture))
        {
            using var doc = new CanvasDocument(Guid.NewGuid(), 280, 65); var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(280, 65)); bitmap.Erase(SKColors.Transparent);
            var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Brush preview"), new(0, 0, 280, 65), "Brush preview"); doc.Layers.Add(layer);
            var brush = BrushPresetLibrary.All[index].Settings; var points = Enumerable.Range(0, 41).Select(i => new SKPoint(18 + i * 6, (float)(32 - 9 * Math.Sin(i * Math.PI / 20)))).ToArray();
            var samples = Enumerable.Range(0, 41).Select(i => new PenSample((float)(.3 + .7 * Math.Sin(Math.PI * (i + 1) / 42)))).ToArray();
            BrushEdits.Paint(doc, layer.ID, points, brush with { Diameter = 23, Red = 1, Green = 1, Blue = 1, Seed = 105, Painting = brush.Painting! with { Samples = samples } });
            picture = CanvasView.ToImage(layer.Asset!.Image); Pictures[index] = picture;
        }
        context.DrawImage(picture, new Rect(0, 0, picture.PixelSize.Width, picture.PixelSize.Height), new Rect(Bounds.Size));
    }
}
