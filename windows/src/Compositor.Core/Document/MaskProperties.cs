using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Mask-only edits never bake density/feather into the original coverage.</summary>
public static class MaskProperties
{
    /// <summary>Coverage in a target pixel grid, including rotated/unlinked masks and soft edges outside the source raster.</summary>
    public static SKBitmap Coverage(LayerMask mask, LayerTransform placement, LayerTransform target, int width, int height)
    {
        var toDocument = BrushEdits.PixelToDocument(target, width, height);
        if (!toDocument.TryInvert(out var toTarget)) throw new ArgumentException("Invalid target transform", nameof(target));
        var radius = mask.Feather * Math.Max(width / target.Width, height / target.Height) / 2;
        var pad = (int)Math.Ceiling(Math.Min(radius * 3, 3000));
        using var expanded = new SKBitmap(Bitmaps.MaskInfo(width + pad * 2, height + pad * 2));
        expanded.Erase(SKColors.Black);
        using (var canvas = new SKCanvas(expanded))
        {
            var map = SKMatrix.Concat(SKMatrix.CreateTranslation(pad, pad), SKMatrix.Concat(toTarget,
                BrushEdits.PixelToDocument(placement, mask.Asset.Width, mask.Asset.Height)));
            canvas.SetMatrix(map);
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
            if (mask.VectorPath is { } pathData)
            {
                using var path = SKPath.ParseSvgPathData(pathData);
                if (path is not null) canvas.DrawPath(path, paint);
            }
            else canvas.DrawBitmap(mask.Asset.Image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), paint);
        }
        if (radius > 0) Pixels.GaussianBlur.Clamped(expanded.GetPixelSpan(), expanded.Width, expanded.Height, 1, expanded.RowBytes, radius);
        var result = new SKBitmap(Bitmaps.MaskInfo(width, height));
        var source = expanded.GetPixelSpan(); var pixels = result.GetPixelSpan();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            pixels[y * result.RowBytes + x] = (byte)Math.Clamp(Math.Round(255 - (255 - source[(y + pad) * expanded.RowBytes + x + pad]) * mask.Density), 0, 255);
        return result;
    }

    public static bool Set(CanvasDocument document, Guid id, double density, double feather)
    {
        if (!double.IsFinite(density) || density is < 0 or > 1 || !double.IsFinite(feather) || feather is < 0 or > 1000
            || document.Layers.FirstOrDefault(layer => layer.ID == id)?.Mask is not { } mask) return false;
        if (mask.Density == density && mask.Feather == feather) return false;
        mask.Density = density; mask.Feather = feather; return true;
    }

    public static bool Invert(CanvasDocument document, Guid id)
    {
        if (document.Layers.FirstOrDefault(layer => layer.ID == id) is not { Mask: { } mask } layer) return false;
        var image = mask.Asset.Image.Copy(SKColorType.Gray8);
        var pixels = image.GetPixelSpan();
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++) pixels[y * image.RowBytes + x] = (byte)(255 - pixels[y * image.RowBytes + x]);
        layer.Mask = mask.Replacing(LayerMask.AssetFrom(image).Asset);
        return true;
    }

    public static bool Refine(CanvasDocument document, Guid id, double density, double feather, int shift, double smooth)
    {
        if (document.Layers.FirstOrDefault(layer => layer.ID == id) is not { Mask: { } mask } layer
            || shift is < -100 or > 100 || !double.IsFinite(smooth) || smooth is < 0 or > 100
            || !double.IsFinite(density) || density is < 0 or > 1 || !double.IsFinite(feather) || feather is < 0 or > 1000) return false;
        if (shift == 0 && smooth == 0) return Set(document, id, density, feather);
        var width = mask.Asset.Width; var height = mask.Asset.Height;
        var image = mask.Asset.Image.Copy(SKColorType.Gray8);
        if (shift != 0)
        {
            var values = image.GetPixelSpan();
            var row = new byte[width]; var column = new byte[height];
            for (var y = 0; y < height; y++)
            {
                values.Slice(y * image.RowBytes, width).CopyTo(row);
                Extremum(row, Math.Abs(shift), shift > 0).AsSpan().CopyTo(values.Slice(y * image.RowBytes, width));
            }
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++) column[y] = values[y * image.RowBytes + x];
                var filtered = Extremum(column, Math.Abs(shift), shift > 0);
                for (var y = 0; y < height; y++) values[y * image.RowBytes + x] = filtered[y];
            }
        }
        if (smooth > 0) Pixels.GaussianBlur.Clamped(image.GetPixelSpan(), width, height, 1, image.RowBytes, smooth / 2);
        layer.Mask = mask.Replacing(LayerMask.AssetFrom(image).Asset);
        Set(document, id, density, feather); return true;
    }

    private static byte[] Extremum(byte[] input, int radius, bool maximum)
    {
        var output = new byte[input.Length]; var queue = new int[input.Length + radius * 2]; var head = 0; var tail = 0;
        byte At(int index) => index < 0 || index >= input.Length ? (byte)0 : input[index];
        for (var i = -radius; i < input.Length + radius; i++)
        {
            while (head < tail && queue[head] < i - radius * 2) head++;
            while (head < tail && (maximum ? At(queue[tail - 1]) <= At(i) : At(queue[tail - 1]) >= At(i))) tail--;
            queue[tail++] = i;
            if (i >= radius) output[i - radius] = At(queue[head]);
        }
        return output;
    }

    public static bool FromSelection(CanvasDocument document, Guid id, bool vector = false)
    {
        if (document.Layers.FirstOrDefault(layer => layer.ID == id) is not { } layer) return false;
        var transform = layer.IsGroup ? new LayerTransform(0, 0, document.Width, document.Height) : layer.Transform;
        var width = layer.Asset?.Width ?? (int)Math.Ceiling(transform.Width);
        var height = layer.Asset?.Height ?? (int)Math.Ceiling(transform.Height);
        if (width <= 0 || height <= 0 || (long)width * height > DocumentLimits.MaxSurfacePixels) return false;
        if (!BrushEdits.PixelToDocument(transform, width, height).TryInvert(out var toMask)) return false;
        var image = new SKBitmap(Bitmaps.MaskInfo(width, height)); image.Erase(SKColors.Black);
        string? pathData = null;
        using (var canvas = new SKCanvas(image))
        {
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            if (vector)
            {
                using var builder = new SKPathBuilder();
                if (document.Selection.Path is { } selected) builder.AddPath(selected);
                else builder.AddRect(SKRect.Create(0, 0, document.Width, document.Height));
                using var path = builder.Detach();
                path.Transform(toMask); pathData = path.ToSvgPathData(); canvas.DrawPath(path, paint);
            }
            else if (document.Selection.Path is null) canvas.Clear(SKColors.White);
            else
            {
                using var coverage = document.Selection.Coverage(SKRectI.Create(0, 0, document.Width, document.Height));
                canvas.SetMatrix(toMask); canvas.DrawBitmap(coverage!, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), paint);
            }
        }
        var previous = layer.Mask;
        layer.Mask = LayerMask.AssetFrom(image);
        if (previous is not null)
        {
            layer.Mask.IsEnabled = previous.IsEnabled; layer.Mask.IsLinked = previous.IsLinked;
            layer.Mask.Density = previous.Density; layer.Mask.Feather = previous.Feather;
        }
        layer.Mask.VectorPath = pathData;
        if (layer.IsGroup) layer.Mask.Placement = transform;
        return true;
    }

    public static bool LoadSelection(CanvasDocument document, Guid id)
    {
        if (document.Layers.FirstOrDefault(layer => layer.ID == id) is not { Mask: { } mask } layer
            || (long)document.Width * document.Height > DocumentLimits.MaxSurfacePixels) return false;
        var coverage = Coverage(mask, layer.MaskTransform, new LayerTransform(0, 0, document.Width, document.Height), document.Width, document.Height);
        document.Selection = DocumentSelection.FromCoverage(coverage); return true;
    }

    public static bool Apply(CanvasDocument document, Guid id)
    {
        if (document.Layers.FirstOrDefault(layer => layer.ID == id) is not { Asset: { } asset, Mask: { } mask, IsGroup: false } layer) return false;
        var image = asset.Image.Copy();
        using var coverage = Coverage(mask, layer.MaskTransform, layer.Transform, image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var amount = coverage.GetPixel(x, y).Red;
            var color = image.GetPixel(x, y); image.SetPixel(x, y, color.WithAlpha((byte)(color.Alpha * amount / 255)));
        }
        layer.Asset = ImportedImage.Create(image, layer.Name); layer.Mask = null; layer.Text = null; layer.Shape = null; return true;
    }
}
