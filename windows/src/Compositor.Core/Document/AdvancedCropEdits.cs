using System.Runtime.InteropServices;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class AdvancedCropEdits
{
    /// <summary>Destructive crop clears discarded raster pixels; vector/text/smart-object sources stay editable.</summary>
    public static bool Apply(CanvasDocument doc, SKRectI frame, bool deletePixels, bool contentAware = false, int width = 0, int height = 0, double resolution = 0)
    {
        if (!CropEdits.Valid(frame) || width < 0 || height < 0 || width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide || !double.IsFinite(resolution) || resolution is < 0 or > 9600) return false;
        if ((long)frame.Width * frame.Height > DocumentLimits.MaxSurfacePixels) return false;
        var oldWidth = doc.Width; var oldHeight = doc.Height;
        using var extension = contentAware ? MakeExtension(doc, frame) : null;
        if (contentAware && extension is null && (frame.Left < 0 || frame.Top < 0 || frame.Right > oldWidth || frame.Bottom > oldHeight)) return false;
        var changed = CanvasEdits.Crop(doc, frame);
        if (deletePixels)
        {
            foreach (var layer in doc.Layers)
            {
                if (layer.Asset is not { } asset || layer.IsGroup || layer.Text is not null || layer.Shape is not null || layer.SmartObject is not null) continue;
                var map = BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height);
                var pixels = DeepPixels.From(asset.WorkingImage); var data = MemoryMarshal.Cast<byte, float>(pixels.GetPixelSpan()); var cleared = false;
                for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++)
                {
                    var at = map.MapPoint(x + .5f, y + .5f); if (at.X >= 0 && at.Y >= 0 && at.X < doc.Width && at.Y < doc.Height) continue;
                    var i = y * pixels.RowBytes / 4 + x * 4; if (data[i + 3] == 0) continue;
                    data.Slice(i, 4).Clear(); cleared = true;
                }
                if (!cleared) { pixels.Dispose(); continue; }
                if (asset.Deep is not null) layer.Asset = ImportedImage.CreateDeep(pixels, asset.Name, asset.BitDepth);
                else { layer.Asset = ImportedImage.Create(DeepPixels.Preview(pixels), asset.Name); pixels.Dispose(); }
                changed = true;
            }
        }
        if (extension is not null) { doc.Layers.Insert(0, new ImageLayer(Guid.NewGuid(), ImportedImage.Create(extension.Copy(), "Content-Aware Fill"), new LayerTransform(0, 0, doc.Width, doc.Height), "Content-Aware Fill")); changed = true; }
        if (width > 0 && height > 0) changed |= ImageEdits.Resize(doc, width, height, resolution > 0 ? resolution : doc.Resolution);
        else if (resolution > 0 && doc.Resolution != resolution) { doc.Resolution = resolution; changed = true; }
        return changed;
    }
    private static SKBitmap? MakeExtension(CanvasDocument doc, SKRectI frame)
    {
        if (frame.Left >= 0 && frame.Top >= 0 && frame.Right <= doc.Width && frame.Bottom <= doc.Height) return null;
        using var source = DocumentRenderer.Render(doc);
        var image = Bitmaps.Allocate(Bitmaps.ColorInfo(frame.Width, frame.Height)); image.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(image)) canvas.DrawBitmap(source, -frame.Left, -frame.Top, new SKSamplingOptions(SKFilterMode.Nearest));
        var mask = new byte[checked(frame.Width * frame.Height)];
        for (var y = 0; y < frame.Height; y++) for (var x = 0; x < frame.Width; x++)
            if (x + frame.Left < 0 || y + frame.Top < 0 || x + frame.Left >= doc.Width || y + frame.Top >= doc.Height) mask[y * frame.Width + x] = 255;
        if (ContentFill.Fill(image.GetPixelSpan(), image.RowBytes, mask, frame.Width, frame.Width, frame.Height) == 0) { image.Dispose(); return null; }
        using (var canvas = new SKCanvas(image)) { canvas.ClipRect(SKRect.Create(-frame.Left, -frame.Top, doc.Width, doc.Height)); canvas.Clear(SKColors.Transparent); }
        return image;
    }
}
