using System.Runtime.InteropServices;
using Compositor.Core.IO;
using Compositor.Core.Model;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Core.Document;

public static class AdvancedEdit
{
    public static SKBitmap ContentAwareResize(SKBitmap source, int width, int height)
    {
        if (width < 1 || height < 1 || width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide || (long)width * height > DocumentLimits.MaxSurfacePixels)
            throw new ArgumentOutOfRangeException(nameof(width));
        using var image = DeepImageCodec.Magick(source);
        image.LiquidRescale(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
        return Pixels(image);
    }
    public static SKBitmap Pixels(MagickImage image)
    {
        image.ColorType = ColorType.TrueColorAlpha;
        using var pixels = image.GetPixels(); var values = pixels.ToArray() ?? throw new InvalidDataException("Empty image.");
        var result = Bitmaps.Allocate(DeepPixels.Info((int)image.Width, (int)image.Height));
        var output = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        if (output.Length != values.Length) { result.Dispose(); throw new InvalidDataException("Unexpected image channel layout."); }
        for (var i = 0; i < output.Length; i++) output[i] = values[i] / 65535f;
        return result;
    }

    // Translation registration by normalized luminance correlation. Downsampling bounds the search cost;
    // alpha is excluded, so transparent borders cannot pull a photograph into the wrong position.
    public static SKPoint AlignTranslation(SKBitmap reference, SKBitmap moving, int maxShift)
    {
        var scale = Math.Min(1, 160.0 / Math.Max(reference.Width, reference.Height));
        var w = Math.Max(1, (int)Math.Round(reference.Width * scale)); var h = Math.Max(1, (int)Math.Round(reference.Height * scale));
        using var a = reference.Resize(Bitmaps.ColorInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear))!;
        using var b = moving.Resize(Bitmaps.ColorInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear))!;
        var limit = Math.Clamp((int)Math.Round(maxShift * scale), 1, 48); var best = double.NegativeInfinity; var bestX = 0; var bestY = 0;
        for (var dy = -limit; dy <= limit; dy++) for (var dx = -limit; dx <= limit; dx++)
        {
            double sumA = 0, sumB = 0, squareA = 0, squareB = 0, product = 0; var count = 0;
            for (var y = Math.Max(0, dy); y < Math.Min(h, h + dy); y += 2)
                for (var x = Math.Max(0, dx); x < Math.Min(w, w + dx); x += 2)
                {
                    var p = a.GetPixel(x, y); var q = b.GetPixel(x - dx, y - dy);
                    if (p.Alpha < 128 || q.Alpha < 128) continue;
                    var v = .2126 * p.Red + .7152 * p.Green + .0722 * p.Blue; var u = .2126 * q.Red + .7152 * q.Green + .0722 * q.Blue;
                    sumA += v; sumB += u; squareA += v * v; squareB += u * u; product += v * u; count++;
                }
            if (count < Math.Max(16, w * h / 32)) continue;
            var denominator = Math.Sqrt(Math.Max(0, squareA - sumA * sumA / count) * Math.Max(0, squareB - sumB * sumB / count));
            var correlation = denominator < 1e-6 ? double.NegativeInfinity : (product - sumA * sumB / count) / denominator;
            correlation -= (Math.Abs(dx) + Math.Abs(dy)) * 1e-7;
            if (correlation > best) { best = correlation; bestX = dx; bestY = dy; }
        }
        return new SKPoint((float)(bestX / scale), (float)(bestY / scale));
    }
    public static SKBitmap Blend(IReadOnlyList<SKBitmap> sources, bool focus)
    {
        if (sources.Count < 2 || sources.Any(s => s.Width != sources[0].Width || s.Height != sources[0].Height)) throw new ArgumentException("Use at least two aligned images of the same size.");
        var w = sources[0].Width; var h = sources[0].Height;
        var values = sources.Select(s => { using var deep = DeepPixels.From(s); return MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan()).ToArray(); }).ToArray();
        var result = Bitmaps.Allocate(DeepPixels.Info(w, h)); var output = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        float Luma(float[] p, int x, int y) { var i = (Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)) * 4; return p[i] * .2126f + p[i + 1] * .7152f + p[i + 2] * .0722f; }
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4; float total = 0, alpha = 0;
            foreach (var p in values)
            {
                var weight = focus ? .001f + Math.Abs(4 * Luma(p, x, y) - Luma(p, x - 1, y) - Luma(p, x + 1, y) - Luma(p, x, y - 1) - Luma(p, x, y + 1)) : 1;
                weight *= p[i + 3]; total += weight; alpha = Math.Max(alpha, p[i + 3]);
                for (var c = 0; c < 3; c++) output[i + c] += p[i + c] * weight;
            }
            if (total > 0) for (var c = 0; c < 3; c++) output[i + c] /= total;
            output[i + 3] = alpha;
        }
        return result;
    }
}
