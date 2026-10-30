using System.Runtime.InteropServices;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Pixels;

/// <summary>
/// One channel of an image as floats, rows top-down, for the lighting filters the Mac build runs as Metal kernels
/// inside Core Image. Blurs are three box passes, which keep a wide Gaussian as quick as a narrow one.
/// </summary>
internal static class LightPlanes
{
    public static float Linear(float v) => v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
    public static float Gamma(float v) => v <= .0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - .055f;
    public static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The working grid for <paramref name="width"/>×<paramref name="height"/>, its longer side at most
    /// <paramref name="limit"/>.</summary>
    public static (int Width, int Height, double Scale) Grid(int width, int height, int limit)
    {
        var scale = Math.Min(1, limit / (double)Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)), scale);
    }

    /// <summary>The source's unpremultiplied RGBA floats (as stored, not linearized) at the working size.</summary>
    public static float[] Rgba(SKBitmap source, int width, int height)
    {
        using var deep = DeepPixels.From(source);
        using var small = width == deep.Width && height == deep.Height ? null : Bitmaps.Allocate(DeepPixels.Info(width, height));
        if (small is not null)
            using (var canvas = new SKCanvas(small))
                canvas.DrawBitmap(deep, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        var from = small ?? deep;
        var data = new float[width * height * 4];
        var span = MemoryMarshal.Cast<byte, float>(from.GetPixelSpan());
        for (var y = 0; y < height; y++) span.Slice(y * from.RowBytes / 4, width * 4).CopyTo(data.AsSpan(y * width * 4));
        return data;
    }

    /// <summary>A Gray8 mask's coverage, 0–1, at the working size.</summary>
    public static float[] Mask(SKBitmap mask, int width, int height)
    {
        if (!Bitmaps.IsValidMask(mask)) throw new ArgumentException("Expected a Gray8 mask.", nameof(mask));
        using var scaled = mask.Width == width && mask.Height == height ? null : Bitmaps.Scale(mask, width, height);
        var from = scaled ?? mask;
        var bytes = from.GetPixelSpan();
        var data = new float[width * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) data[y * width + x] = bytes[y * from.RowBytes + x] / 255f;
        return data;
    }

    /// <summary>A Gaussian of <paramref name="sigma"/>, edges clamped, approximated by three box blurs.</summary>
    public static float[] Blur(float[] plane, int width, int height, double sigma)
    {
        var result = (float[])plane.Clone();
        if (sigma < .3) return result;
        var scratch = new float[plane.Length];
        foreach (var size in Boxes(sigma))
        {
            var r = (size - 1) / 2;
            Box(result, scratch, width, height, r, horizontal: true);
            Box(scratch, result, width, height, r, horizontal: false);
        }
        return result;
    }

    /// <summary>Three box widths whose passes add up to a Gaussian of <paramref name="sigma"/>.</summary>
    private static int[] Boxes(double sigma)
    {
        var ideal = Math.Sqrt(12 * sigma * sigma / 3 + 1);
        var lower = (int)Math.Floor(ideal);
        if (lower % 2 == 0) lower--;
        var upper = lower + 2;
        var m = (int)Math.Round((12 * sigma * sigma - 3 * lower * lower - 12 * lower - 9) / (-4.0 * lower - 4));
        return [.. Enumerable.Range(0, 3).Select(i => i < m ? lower : upper)];
    }

    private static void Box(float[] source, float[] target, int width, int height, int r, bool horizontal)
    {
        var lines = horizontal ? height : width;
        var length = horizontal ? width : height;
        var step = horizontal ? 1 : width;
        var scale = 1f / (r + r + 1);
        Parallel.For(0, lines, line =>
        {
            var start = horizontal ? line * width : line;
            float At(int i) => source[start + Math.Clamp(i, 0, length - 1) * step];
            double sum = 0;
            for (var i = -r - 1; i < r; i++) sum += At(i);
            for (var i = 0; i < length; i++)
            {
                sum += At(i + r) - At(i - r - 1);
                target[start + i * step] = (float)(sum * scale);
            }
        });
    }

    /// <summary>One plane sampled between its pixels, rows top-down, edges clamped.</summary>
    public static float Sample(float[] plane, int width, int height, float x, float y)
    {
        x = Math.Clamp(x - .5f, 0, width - 1); y = Math.Clamp(y - .5f, 0, height - 1);
        var ix = (int)x; var iy = (int)y; var fx = x - ix; var fy = y - iy;
        var ix1 = Math.Min(width - 1, ix + 1); var iy1 = Math.Min(height - 1, iy + 1);
        var a = plane[iy * width + ix]; var b = plane[iy * width + ix1];
        var c = plane[iy1 * width + ix]; var d = plane[iy1 * width + ix1];
        return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
    }

    /// <summary>A plane worked out on a smaller grid, read at a pixel of the full one.</summary>
    public static float Up(float[] plane, int width, int height, double scale, int x, int y) =>
        scale >= 1 ? plane[y * width + x] : Sample(plane, width, height, (float)((x + .5) * scale), (float)((y + .5) * scale));

    /// <summary>
    /// A soft mask pulled onto the guide's edges (He, Sun and Tang's guided filter over the guide's gray), as the
    /// Mac build's GuidedMatte refines Vision's mask before lighting it.
    /// </summary>
    public static float[] Guided(float[] mask, float[] guide, int width, int height, int radius, float epsilon = 1e-3f)
    {
        float[] Mean(float[] plane) => BoxMean(plane, width, height, radius);
        var product = new float[mask.Length]; var square = new float[mask.Length];
        for (var i = 0; i < mask.Length; i++) { product[i] = guide[i] * mask[i]; square[i] = guide[i] * guide[i]; }
        var meanI = Mean(guide); var meanP = Mean(mask); var meanIp = Mean(product); var meanII = Mean(square);
        var a = new float[mask.Length]; var b = new float[mask.Length];
        for (var i = 0; i < mask.Length; i++)
        {
            var variance = meanII[i] - meanI[i] * meanI[i];
            a[i] = (meanIp[i] - meanI[i] * meanP[i]) / (variance + epsilon);
            b[i] = meanP[i] - a[i] * meanI[i];
        }
        var meanA = Mean(a); var meanB = Mean(b);
        var result = new float[mask.Length];
        for (var i = 0; i < mask.Length; i++) result[i] = Math.Clamp(meanA[i] * guide[i] + meanB[i], 0, 1);
        return result;
    }

    /// <summary>The mean over a (2r+1)² square, from an integral image, edges shrinking the square.</summary>
    private static float[] BoxMean(float[] plane, int width, int height, int r)
    {
        var integral = new double[(width + 1) * (height + 1)];
        for (var y = 0; y < height; y++)
        {
            double row = 0;
            for (var x = 0; x < width; x++)
            {
                row += plane[y * width + x];
                integral[(y + 1) * (width + 1) + x + 1] = integral[y * (width + 1) + x + 1] + row;
            }
        }
        var result = new float[plane.Length];
        Parallel.For(0, height, y =>
        {
            var y0 = Math.Max(0, y - r); var y1 = Math.Min(height, y + r + 1);
            for (var x = 0; x < width; x++)
            {
                var x0 = Math.Max(0, x - r); var x1 = Math.Min(width, x + r + 1);
                var sum = integral[y1 * (width + 1) + x1] - integral[y0 * (width + 1) + x1] - integral[y1 * (width + 1) + x0] + integral[y0 * (width + 1) + x0];
                result[y * width + x] = (float)(sum / ((x1 - x0) * (y1 - y0)));
            }
        });
        return result;
    }
}
