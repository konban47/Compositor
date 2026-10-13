using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public sealed record MaskRefinementSettings(double Radius = 0, bool SmartRadius = false, bool ObjectAware = false,
    double Smooth = 0, double Feather = 0, double Contrast = 0, double ShiftPercent = 0,
    bool Invert = false, bool Clear = false, bool Decontaminate = false, double Amount = 100);

public static class MaskRefinement
{
    public static bool Apply(CanvasDocument doc, Guid id, MaskRefinementSettings settings)
    {
        if (doc.Layers.FirstOrDefault(l => l.ID == id) is not { Mask: { } mask } layer) return false;
        if (new[] { settings.Radius, settings.Smooth, settings.Feather, settings.Contrast, settings.ShiftPercent, settings.Amount }.Any(n => !double.IsFinite(n))
            || settings.Radius is < 0 or > 100 || settings.Smooth is < 0 or > 100 || settings.Feather is < 0 or > 1000
            || settings.Contrast is < 0 or > 100 || settings.ShiftPercent is < -100 or > 100 || settings.Amount is < 0 or > 100) return false;
        var image = mask.Asset.Image.Copy(SKColorType.Gray8);
        if (settings.Clear) image.Erase(SKColors.Black);
        if (settings.Radius > 0 && !settings.Clear)
        {
            using var source = image.Copy(); var src = source.GetPixelSpan(); var dst = image.GetPixelSpan();
            var radius = Math.Max(1, (int)Math.Round(settings.Radius));
            var mapping = layer.Asset is { } asset && BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height).TryInvert(out var inverse)
                ? SKMatrix.Concat(inverse, BrushEdits.PixelToDocument(layer.MaskTransform, image.Width, image.Height)) : SKMatrix.Identity;
            SKColor ColorAt(int x, int y)
            {
                if (layer.Asset is not { } pixels) return SKColors.Gray;
                var p = mapping.MapPoint(x, y); return pixels.Image.GetPixel(Math.Clamp((int)p.X, 0, pixels.Width - 1), Math.Clamp((int)p.Y, 0, pixels.Height - 1));
            }
            for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
            {
                var center = src[y * source.RowBytes + x]; var color = ColorAt(x, y); double sum = center * 2, weight = 2;
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = Math.Clamp(x + dx * radius, 0, image.Width - 1); var ny = Math.Clamp(y + dy * radius, 0, image.Height - 1);
                    var sample = src[ny * source.RowBytes + nx]; var other = ColorAt(nx, ny);
                    var difference = settings.ObjectAware ? Math.Abs(color.Red * .2126 + color.Green * .7152 + color.Blue * .0722 - other.Red * .2126 - other.Green * .7152 - other.Blue * .0722)
                        : (Math.Abs(color.Red - other.Red) + Math.Abs(color.Green - other.Green) + Math.Abs(color.Blue - other.Blue)) / 3.0;
                    var w = 1 / (1 + difference * difference / 400);
                    if (settings.SmartRadius && Math.Abs(center - sample) < 10) w *= .25;
                    sum += sample * w; weight += w;
                }
                dst[y * image.RowBytes + x] = (byte)Math.Round(sum / weight);
            }
        }
        layer.Mask = mask.Replacing(ImportedImage.Create(image, mask.Asset.Name)); layer.Mask.VectorPath = null; layer.Mask.VectorEvenOdd = false;
        var shift = (int)Math.Round(settings.ShiftPercent / 100 * Math.Max(1, Math.Max(settings.Radius, settings.Feather)));
        var intermediate = layer.Mask.Asset;
        MaskProperties.Refine(doc, id, mask.Density, settings.Feather, Math.Clamp(shift, -100, 100), settings.Smooth);
        if (!ReferenceEquals(intermediate, layer.Mask.Asset)) intermediate.Dispose();
        if (settings.Contrast > 0 || settings.Invert)
        {
            var adjusted = layer.Mask.Asset.Image.Copy(); var span = adjusted.GetPixelSpan();
            var slope = 1 / Math.Max(.01, 1 - settings.Contrast / 100);
            for (var y = 0; y < adjusted.Height; y++) for (var x = 0; x < adjusted.Width; x++)
            { var value = Math.Clamp((span[y * adjusted.RowBytes + x] / 255.0 - .5) * slope + .5, 0, 1); span[y * adjusted.RowBytes + x] = (byte)Math.Round((settings.Invert ? 1 - value : value) * 255); }
            var previous = layer.Mask.Asset; layer.Mask = layer.Mask.Replacing(ImportedImage.Create(adjusted, mask.Asset.Name)); previous.Dispose();
        }
        if (settings.Decontaminate && layer.Asset is { } original)
        {
            using var coverage = MaskProperties.Coverage(layer.Mask, layer.MaskTransform, layer.Transform, original.Width, original.Height);
            var pixels = DeepPixels.From(original.WorkingImage); var values = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(pixels.GetPixelSpan());
            var radius = Math.Clamp((int)Math.Ceiling(settings.Radius), 1, 8);
            for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++)
            {
                var alpha = coverage.GetPixel(x, y).Red; if (alpha is 0 or 255) continue;
                var nearestX = x; var nearestY = y; var best = int.MaxValue;
                for (var dy = -radius; dy <= radius; dy++) for (var dx = -radius; dx <= radius; dx++)
                { var nx = x + dx; var ny = y + dy; var distance = dx * dx + dy * dy; if (nx < 0 || ny < 0 || nx >= pixels.Width || ny >= pixels.Height || distance >= best || coverage.GetPixel(nx, ny).Red < 250) continue; nearestX = nx; nearestY = ny; best = distance; }
                var amount = settings.Amount / 100 * (1 - alpha / 255.0);
                var from = y * pixels.RowBytes / 4 + x * 4; var to = nearestY * pixels.RowBytes / 4 + nearestX * 4;
                for (var c = 0; c < 3; c++) values[from + c] += (values[to + c] - values[from + c]) * (float)amount;
            }
            if (original.Deep is not null) layer.Asset = ImportedImage.CreateDeep(pixels, layer.Name, original.BitDepth);
            else { layer.Asset = ImportedImage.Create(DeepPixels.Preview(pixels), layer.Name); pixels.Dispose(); }
            layer.Text = null; layer.Shape = null;
        }
        return true;
    }
}
