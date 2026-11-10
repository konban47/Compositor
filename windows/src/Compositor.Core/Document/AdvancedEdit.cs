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
    public static SKPoint AlignTranslation(SKBitmap reference, SKBitmap moving, int maxShift, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (reference.Width != moving.Width || reference.Height != moving.Height) throw new ArgumentException("Alignment requires equally sized canvases.");
        if (maxShift < 0) throw new ArgumentOutOfRangeException(nameof(maxShift));
        if (maxShift == 0) return SKPoint.Empty;
        var scale = Math.Min(1, 160.0 / Math.Max(reference.Width, reference.Height));
        var w = Math.Max(1, (int)Math.Round(reference.Width * scale)); var h = Math.Max(1, (int)Math.Round(reference.Height * scale));
        using var a = reference.Resize(Bitmaps.ColorInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear))!;
        using var b = moving.Resize(Bitmaps.ColorInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear))!;
        var limit = Math.Clamp((int)Math.Round(maxShift * scale), 1, 48); var best = double.NegativeInfinity; var bestX = 0; var bestY = 0;
        for (var dy = -limit; dy <= limit; dy++) for (var dx = -limit; dx <= limit; dx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
    public static SKBitmap Blend(IReadOnlyList<SKBitmap> sources, bool focus, bool seamless = false)
    {
        if (sources.Count < 2 || sources.Any(s => s.Width != sources[0].Width || s.Height != sources[0].Height)) throw new ArgumentException("Use at least two aligned images of the same size.");
        var w = sources[0].Width; var h = sources[0].Height;
        var values = sources.Select(s => { using var deep = DeepPixels.From(s); return MemoryMarshal.Cast<byte, float>(deep.GetPixelSpan()).ToArray(); }).ToArray();
        if (seamless) Match(values);
        // A panorama weighs each picture by how far a pixel is inside it, so its seams fade rather than cut.
        var reach = focus ? null : values.Select(p => Inside(p, w, h, Math.Max(8, Math.Min(w, h) / 40))).ToArray();
        var result = Bitmaps.Allocate(DeepPixels.Info(w, h)); var output = MemoryMarshal.Cast<byte, float>(result.GetPixelSpan());
        float Luma(float[] p, int x, int y) { var i = (Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)) * 4; return p[i] * .2126f + p[i + 1] * .7152f + p[i + 2] * .0722f; }
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4; float total = 0, alpha = 0;
            foreach (var p in values)
            {
                var weight = focus ? .001f + Math.Abs(4 * Luma(p, x, y) - Luma(p, x - 1, y) - Luma(p, x + 1, y) - Luma(p, x, y - 1) - Luma(p, x, y + 1)) : .001f + reach![Array.IndexOf(values, p)][y * w + x];
                weight *= p[i + 3]; total += weight; alpha = Math.Max(alpha, p[i + 3]);
                for (var c = 0; c < 3; c++) output[i + c] += p[i + c] * weight;
            }
            if (total > 0) for (var c = 0; c < 3; c++) output[i + c] /= total;
            output[i + 3] = alpha;
        }
        return result;
    }

    /// <summary>Seamless Tones and Colors: each picture's average color is brought to the average of them all where it has pixels.</summary>
    private static void Match(float[][] values)
    {
        var means = values.Select(p => { double r = 0, g = 0, b = 0, n = 0; for (var i = 0; i < p.Length; i += 4) { var a = p[i + 3]; r += p[i] * a; g += p[i + 1] * a; b += p[i + 2] * a; n += a; } return n > 0 ? (r / n, g / n, b / n) : (0d, 0d, 0d); }).ToArray();
        var target = (R: means.Average(m => m.Item1), G: means.Average(m => m.Item2), B: means.Average(m => m.Item3));
        for (var k = 0; k < values.Length; k++)
        {
            var (r, g, b) = means[k]; float Gain(double from, double to) => from > 1e-4 ? (float)Math.Clamp(to / from, .5, 2) : 1;
            var gains = new[] { Gain(r, target.R), Gain(g, target.G), Gain(b, target.B) }; var p = values[k];
            for (var i = 0; i < p.Length; i += 4) for (var c = 0; c < 3; c++) p[i + c] *= gains[c];
        }
    }

    /// <summary>How far inside its opaque area each pixel is, 0 at the edge and 1 a radius or more in, from a box blur of the opacity.</summary>
    private static float[] Inside(float[] p, int w, int h, int radius)
    {
        var sums = new double[(w + 1) * (h + 1)];
        for (var y = 0; y < h; y++) { double row = 0; for (var x = 0; x < w; x++) { row += p[(y * w + x) * 4 + 3] >= .5f ? 1 : 0; sums[(y + 1) * (w + 1) + x + 1] = sums[y * (w + 1) + x + 1] + row; } }
        var result = new float[w * h];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            int x0 = Math.Max(0, x - radius), y0 = Math.Max(0, y - radius), x1 = Math.Min(w, x + radius + 1), y1 = Math.Min(h, y + radius + 1);
            var area = (x1 - x0) * (y1 - y0); var inside = sums[y1 * (w + 1) + x1] - sums[y0 * (w + 1) + x1] - sums[y1 * (w + 1) + x0] + sums[y0 * (w + 1) + x0];
            result[y * w + x] = p[(y * w + x) * 4 + 3] * (float)Math.Clamp((inside / area - .5) * 2, 0, 1);
        }
        return result;
    }
}
